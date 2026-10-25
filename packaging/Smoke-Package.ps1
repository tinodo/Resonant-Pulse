[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Package,
    [string]$Screenshot = 'artifacts\packaged-startup\startup.png'
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or $env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'Certificate trust and package installation are restricted to disposable GitHub-hosted Windows runners.'
}
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'The disposable runner must be elevated to trust its temporary test certificate.'
}
$packagePath = (Resolve-Path -LiteralPath $Package).Path
$archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $entry = $archive.GetEntry('AppxManifest.xml')
    if (-not $entry) { throw 'The package has no AppxManifest.xml.' }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { $manifest = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
} finally { $archive.Dispose() }
$identityName = [string]$manifest.Package.Identity.Name
$publisher = [string]$manifest.Package.Identity.Publisher
$applicationId = [string]$manifest.Package.Applications.Application.Id
if (@($manifest.Package.Applications.Application).Count -ne 1 -or
    [string]::IsNullOrWhiteSpace($identityName) -or [string]::IsNullOrWhiteSpace($publisher) -or
    [string]::IsNullOrWhiteSpace($applicationId) -or [WildcardPattern]::ContainsWildcardCharacters($identityName)) {
    throw 'The package must contain one application with a concrete identity and publisher.'
}
if (Get-AppxPackage -Name $identityName) {
    throw 'An existing package has this identity; the smoke test will not replace it.'
}
$artifactDirectory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Screenshot))
$null = [IO.Directory]::CreateDirectory($artifactDirectory)
$temporaryDirectory = Join-Path $env:RUNNER_TEMP ("pulse-package-smoke-" + [Guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($temporaryDirectory)
$certificate = $null
$trustedCertificate = $null
$activatedProcess = $null
$installationAttempted = $false
$failures = [Collections.Generic.List[Exception]]::new()
try {
    $kitRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $signTool = $null
    foreach ($directory in (Get-ChildItem -LiteralPath $kitRoot -Directory |
        Where-Object { $_.Name -match '^\d+\.' } | Sort-Object { [version]$_.Name } -Descending)) {
        $candidate = Join-Path $directory.FullName 'x64\signtool.exe'
        if (Test-Path -LiteralPath $candidate) { $signTool = $candidate; break }
    }
    if (-not $signTool) { throw 'Windows SDK SignTool is unavailable on this runner.' }
    $certificate = New-SelfSignedCertificate -Type Custom -Subject $publisher -KeyUsage DigitalSignature `
        -CertStoreLocation 'Cert:\CurrentUser\My' -NotAfter (Get-Date).AddDays(1) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3','2.5.29.19={text}') `
        -FriendlyName 'Resonant Pulse disposable CI test only'
    $certificatePath = Join-Path $temporaryDirectory 'test.cer'
    $null = Export-Certificate -Cert $certificate -FilePath $certificatePath
    $trustedCertificate = Import-Certificate -FilePath $certificatePath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'
    $signedCopy = Join-Path $temporaryDirectory 'test.msix'
    Copy-Item -LiteralPath $packagePath -Destination $signedCopy
    & $signTool sign /fd SHA256 /s My /sha1 $certificate.Thumbprint $signedCopy
    if ($LASTEXITCODE -ne 0) { throw "Temporary package signing failed: $LASTEXITCODE." }
    $installationAttempted = $true
    Add-AppxPackage -Path $signedCopy
    $installed = Get-AppxPackage -Name $identityName
    if (-not $installed -or $installed.Publisher -ne $publisher) {
        throw 'Installation did not produce the expected package identity.'
    }
    Add-Type @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
public static class PulsePackageActivation
{
    [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
    }
    [ComImport, Guid("45ba127d-10a8-46ea-8ab7-56ea9078943c")]
    private class ApplicationActivationManager { }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFullName(IntPtr process, ref uint length, StringBuilder packageFullName);
    public static uint Activate(string appUserModelId)
    {
        var manager = (IApplicationActivationManager)new ApplicationActivationManager();
        try
        {
            int result = manager.ActivateApplication(appUserModelId, "", 2, out uint processId);
            Marshal.ThrowExceptionForHR(result);
            return processId;
        }
        finally { Marshal.ReleaseComObject(manager); }
    }
    public static string PackageIdentity(IntPtr process)
    {
        uint length = 0;
        int result = GetPackageFullName(process, ref length, null);
        if (result != 122) throw new Win32Exception(result, "The activated process has no readable package identity.");
        var name = new StringBuilder(checked((int)length));
        result = GetPackageFullName(process, ref length, name);
        if (result != 0) throw new Win32Exception(result);
        return name.ToString();
    }
}
'@
    $activationPid = [PulsePackageActivation]::Activate("$($installed.PackageFamilyName)!$applicationId")
    if (-not $activationPid) { throw 'Activation returned no application process ID.' }
    $activatedProcess = [Diagnostics.Process]::GetProcessById([int]$activationPid)
    $actualIdentity = [PulsePackageActivation]::PackageIdentity($activatedProcess.Handle)
    if ($actualIdentity -ne $installed.PackageFullName) {
        throw 'Activation started a process with the wrong package identity.'
    }
    @{packageFullName=$actualIdentity;publisher=$installed.Publisher;activation='ApplicationActivationManager'} |
        ConvertTo-Json | Set-Content (Join-Path $artifactDirectory 'package-identity.json') -Encoding utf8
    & (Join-Path $PSScriptRoot 'Smoke-Launch.ps1') -Payload $installed.InstallLocation `
        -ExistingProcessId $activationPid -PackageFamilyName $installed.PackageFamilyName -Screenshot $Screenshot
} catch {
    $failures.Add($_.Exception)
} finally {
    # Preserve both the primary failure and any cleanup failure.
    foreach ($cleanup in @(
        { if ($activatedProcess) { try { $activatedProcess.Refresh(); if (-not $activatedProcess.HasExited) { Stop-Process -Id $activatedProcess.Id -Force } } finally { $activatedProcess.Dispose() } } },
        { if ($installationAttempted) { Get-AppxPackage -Name $identityName | Remove-AppxPackage; if (Get-AppxPackage -Name $identityName) { throw 'The test package remained installed after removal.' } } },
        { if ($trustedCertificate) { Remove-Item -LiteralPath "Cert:\LocalMachine\TrustedPeople\$($trustedCertificate.Thumbprint)" -Force } },
        { if ($certificate) { Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($certificate.Thumbprint)" -DeleteKey -Force } },
        { Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force }
    )) {
        try { & $cleanup } catch { $failures.Add($_.Exception) }
    }
}
if ($failures.Count) {
    throw [AggregateException]::new('Packaged installation/activation verification or cleanup failed.',$failures)
}
Write-Output "Installed, activated with real package identity, captured and uninstalled $identityName."
Write-Output 'The temporary signed copy and trust were removed; the unsigned candidate is unchanged. This is not Store certification or audio acceptance.'
