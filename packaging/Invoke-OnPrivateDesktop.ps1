[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Script,
    [string[]]$ArgumentList = @(),
    [Parameter(Mandatory)][string]$LogPath,
    [int]$TimeoutSeconds = 900
)
# Runs a PowerShell script on a PRIVATE desktop of the current session. Every window it creates (the application under
# test and the UI Automation driver) lives only there, so nothing appears on screen, nobody can click into it, and
# keystrokes or screen copies can never reach another person's windows. Standard output and error go to -LogPath.
# Keyboard injection and screen copying do not work on a private desktop; the native verifier skips both there.
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class PrivateDesktop
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
    [StructLayout(LayoutKind.Sequential)]
    public struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; public bool bInheritHandle; }
    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct IO_COUNTERS { public ulong Reads, Writes, Others, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateJobObjectW(IntPtr attributes, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetInformationJobObject(IntPtr job, int informationClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION information, int length);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool TerminateJobObject(IntPtr job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern uint ResumeThread(IntPtr thread);
    // Built in C# because a nested struct field cannot be assigned through a PowerShell variable.
    public static bool SetKillOnClose(IntPtr job)
    {
        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        limits.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        return SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)));
    }
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateDesktopW(string name, IntPtr device, IntPtr devmode, int flags, uint access, IntPtr attributes);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateFileW(string path, uint access, uint share, ref SECURITY_ATTRIBUTES attributes,
        uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes,
        bool inherit, uint flags, IntPtr environment, string directory, ref STARTUPINFO startup, out PROCESS_INFORMATION info);
    [DllImport("kernel32.dll")] public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")] public static extern bool GetExitCodeProcess(IntPtr handle, out uint code);
    [DllImport("kernel32.dll")] public static extern bool TerminateProcess(IntPtr handle, uint code);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
}
'@
function ConvertTo-CommandLineArgument([string]$value) {
    # CreateProcess quoting: escape embedded quotes and the backslashes in front of them or the closing quote.
    $escaped = [Text.RegularExpressions.Regex]::Replace($value, '(\\*)"', '$1$1\"')
    $escaped = [Text.RegularExpressions.Regex]::Replace($escaped, '(\\+)$', '$1$1')
    '"' + $escaped + '"'
}
$shell = (Get-Process -Id $PID).Path
$scriptPath = (Resolve-Path -LiteralPath $Script).Path
$null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent ([IO.Path]::GetFullPath($LogPath)))
$name = "private-$PID-$([Guid]::NewGuid().ToString('N').Substring(0, 6))"
$desktop = [PrivateDesktop]::CreateDesktopW($name, [IntPtr]::Zero, [IntPtr]::Zero, 0, 0x10000000, [IntPtr]::Zero)
if ($desktop -eq [IntPtr]::Zero) { throw "CreateDesktop failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
$exit = -1
$log = [IntPtr]::Zero
$job = [IntPtr]::Zero
try {
    # Everything started below belongs to a job that kills it when the job is closed or terminated, so a timeout, a
    # crash of this script or Ctrl+C can never leave the application (or its driver) running, invisibly, on the
    # private desktop.
    $job = [PrivateDesktop]::CreateJobObjectW([IntPtr]::Zero, $null)
    if ($job -eq [IntPtr]::Zero) { throw "CreateJobObject failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
    if (-not [PrivateDesktop]::SetKillOnClose($job)) { throw "SetInformationJobObject failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
    $attributes = [PrivateDesktop+SECURITY_ATTRIBUTES]::new()
    $attributes.nLength = [Runtime.InteropServices.Marshal]::SizeOf([type][PrivateDesktop+SECURITY_ATTRIBUTES])
    $attributes.bInheritHandle = $true
    # GENERIC_WRITE, FILE_SHARE_READ, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL
    $log = [PrivateDesktop]::CreateFileW([IO.Path]::GetFullPath($LogPath), 0x40000000, 1, [ref]$attributes, 2, 0x80, [IntPtr]::Zero)
    if ($log -eq [IntPtr]::new(-1)) { throw "Cannot open the log: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
    $startup = [PrivateDesktop+STARTUPINFO]::new()
    $startup.cb = [Runtime.InteropServices.Marshal]::SizeOf([type][PrivateDesktop+STARTUPINFO])
    $startup.lpDesktop = "WinSta0\$name"
    $startup.dwFlags = 0x100 # STARTF_USESTDHANDLES
    $startup.hStdOutput = $log
    $startup.hStdError = $log
    $arguments = @('-NoProfile', '-NonInteractive', '-File', $scriptPath) + $ArgumentList
    $line = [Text.StringBuilder]::new((ConvertTo-CommandLineArgument $shell) + ' ' +
        (($arguments | ForEach-Object { ConvertTo-CommandLineArgument $_ }) -join ' '))
    $info = [PrivateDesktop+PROCESS_INFORMATION]::new()
    # CREATE_NO_WINDOW | CREATE_SUSPENDED: the script is a console program with no console window of its own, and it
    # must belong to the job before it runs a single instruction (children then inherit the membership).
    $created = [PrivateDesktop]::CreateProcessW($shell, $line, [IntPtr]::Zero, [IntPtr]::Zero, $true, 0x08000004, [IntPtr]::Zero,
        (Get-Location).Path, [ref]$startup, [ref]$info)
    if (-not $created) { throw "CreateProcess failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
    try {
        if (-not [PrivateDesktop]::AssignProcessToJobObject($job, $info.hProcess)) {
            $assignError = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
            [void][PrivateDesktop]::TerminateProcess($info.hProcess, 1)
            throw "AssignProcessToJobObject failed: $assignError"
        }
        if ([PrivateDesktop]::ResumeThread($info.hThread) -eq [uint32]::MaxValue) {
            throw "ResumeThread failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
        }
        if ([PrivateDesktop]::WaitForSingleObject($info.hProcess, [uint32]($TimeoutSeconds * 1000)) -ne 0) {
            [void][PrivateDesktop]::TerminateJobObject($job, 1)
            throw "Timed out after $TimeoutSeconds s on the private desktop."
        }
        [uint32]$code = 0
        [void][PrivateDesktop]::GetExitCodeProcess($info.hProcess, [ref]$code)
        $exit = [int]$code
    } finally {
        [void][PrivateDesktop]::CloseHandle($info.hProcess)
        [void][PrivateDesktop]::CloseHandle($info.hThread)
    }
} finally {
    if ($log -ne [IntPtr]::Zero -and $log -ne [IntPtr]::new(-1)) { [void][PrivateDesktop]::CloseHandle($log) }
    # Closing the job kills anything still running in it; Windows closes a desktop only once nothing runs on it.
    if ($job -ne [IntPtr]::Zero) { [void][PrivateDesktop]::CloseHandle($job) }
    foreach ($attempt in 1..20) { if ([PrivateDesktop]::CloseDesktop($desktop)) { break }; Start-Sleep -Milliseconds 250 }
}
"private-desktop exit code: $exit"
exit $exit
