# Delivery and Store operations

## Build and test

The SDK version in `global.json` is exact (`rollForward: disable`), so setup
does not silently follow newly advertised SDK releases before their downloads
are available. SDK upgrades require an explicit reviewed change.
See [.NET SDK selection policy](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json#rollforward).

`ci.yml` runs deterministic Core tests and publishes x64/ARM64 Windows builds,
checks the self-contained payload, creates an unsigned development MSIX and
retains TRX results. The x64 package gate signs a temporary copy, installs it,
activates through its application user model ID, verifies the running process's
package identity, captures the responsive window, and uninstalls it. This runs
only on disposable elevated GitHub-hosted Windows runners. Test trust, private
key and signed copy are removed; no test certificate is shipped. A passing
build/install check is not a passed hardware/audio/Store test.

`release.yml` is manually dispatched from main. It invokes the same complete CI
workflow at that source SHA, then publishes portable ZIPs plus SHA-256 checksums.
Release and Store packaging also require the main-branch CodeQL run for that
exact SHA to succeed; an earlier green security scan is insufficient.
Release versions are single-use: the workflow atomically creates a new tag at
the tested SHA and rejects an existing tag. It never attaches tested binaries
to a different pre-existing source tag. If publication fails after tag creation,
inspect the failure and use a new preview version; do not silently move a tag.
Use a preview semantic version until the owner has accepted the product (hardware/accessibility
acceptance, or his recorded decision to accept it with the gaps stated). Failed gates prevent release publication.
Stable releases additionally require `PRODUCT_ACCEPTANCE_COMPLETED=true`,
configured only after full-product/device acceptance is recorded (for 1.0.0 the
owner's acceptance, with what it rests on and what it does not claim, is the row
"Release acceptance (1.0.0)" of DELIVERY.md, written before the variable was set).
Every ZIP
contains build-info.json with its source SHA, version, architecture and CI run.
Public Store update submission requires the same acceptance flag; generating
an identity-correct candidate does not claim that acceptance or publish it.

Dependabot checks NuGet and GitHub Actions weekly. The repository's active
`Automatic Copilot review` ruleset requests real reviews for main-targeting PRs,
including new pushes. `copilot-review.yml` verifies that native rule and waits
for the real `copilot-pull-request-reviewer[bot]` review of the exact PR head.
It uses the ordinary `pull_request` event with read-only permissions and never
checks out or executes PR code; no owner token is stored, so the check cannot
request a review itself.
A pull request that Dependabot opens gets no automatic review. GitHub describes
automatic review as covering pull requests created by people with access to
Copilot ("Automatic pull request reviews" in
[GitHub Copilot code review](https://docs.github.com/en/copilot/concepts/agent-capabilities/agent-powered-workflows-and-operations/code-review),
read 29 October 2026), and one opened by Dependabot is not one of those: no head
of Dependabot PRs #2 and #3 got an automatic request, on 26 and 29 October 2026,
so the required check waited out its 22.5 minutes and failed. For each Dependabot
head, request the review by hand:

    gh pr edit NUMBER -R tinodo/Resonant-Pulse --add-reviewer '@copilot'

A review of that exact head followed within about two minutes both times and the
check then passed by itself. Request it as soon as the head appears: the check
gives up after 22.5 minutes, and a review that completes later does not wake it,
because the workflow runs only when a pull request is opened, reopened, made
ready or pushed to (true of every pull request, not only Dependabot's). Then
re-run the failed check for the same head once the review has completed, with the
run found by `gh run list -R tinodo/Resonant-Pulse --workflow copilot-review.yml --commit HEAD_SHA`:

    gh run rerun RUN_ID -R tinodo/Resonant-Pulse --failed

None of this is a bypass: the check still demands a completed review of the exact
head, which is read in full and its conversations resolved like any other. After
another merge a Dependabot pull request is out of
date: comment `@dependabot rebase`, or run
`gh pr update-branch NUMBER -R tinodo/Resonant-Pulse` when Dependabot has not
answered within about ten minutes (it did not on PR #2).
A Dependabot pull request that changes the app project can bring a
`packages.lock.json` with a target that only its Linux restore produces: PR #41
(Microsoft.WindowsAppSDK 2.6.4) added `net10.0-windows10.0.26100/linux-x64` to
`src/ResonantPulse.App/packages.lock.json`. A Windows restore removes it again and
the workflow does not restore in locked mode, so it does no harm, and nobody
commits to the Dependabot branch to remove it. After the merge a local Windows
build leaves that file modified, and `src/ResonantPulse.Core/packages.lock.json`
too (it gains a `net10.0/win-x64` target): revert both before a commit.
A missing review or timeout fails visibly. Copilot availability/entitlement is
account-dependent. Completion is not approval or an assertion of zero findings;
review conversations must still be resolved before merge. `Copilot review
completion` is a required branch-protection check, verified end to end in hosted
run 40223399203 without an owner credential.

## GitHub Package

The engine (`src/ResonantPulse.Core`: no user interface, no package dependencies) is also published as the NuGet package
`ResonantPulse.Core` on GitHub Packages (`https://nuget.pkg.github.com/tinodo/index.json`), as the Resonant tuner publishes
`Resonant.Core`. The package version is the version of a release: release 1.0.0 is package 1.0.0, and a pre-release 1.1.0-preview.1 is
package 1.1.0-preview.1. The library is the application's engine and is versioned with it; the package promises no stable API.

- `packaging/Build-NuGetPackage.ps1` packs it and checks it before anything can publish it. The package holds exactly the expected
  files; its nuspec names the id, the version, the MIT license, the readme, the repository and the commit it was built from; it has no
  dependencies; and a stand-alone project (`packaging/nuget/Consumer`, copied out of the repository) restores the package from a local
  folder as its only source, checks that the assembly it runs is byte for byte the one in the package, and runs the quick start of the
  package README against it. `ci.yml` does this in the "Musical correctness" job of every build, for the fixed version `0.0.0-ci`,
  so a change that breaks the package breaks the pull request.
- `release.yml` calls `package.yml` after it has published a release. `package.yml` can also be run by hand from main
  (`gh workflow run package.yml -R tinodo/Resonant-Pulse --ref main -f version=1.0.0`), for a release that was published before it
  existed (1.0.0) or to repeat a push that failed. It refuses a version without a published GitHub release, a release whose pre-release
  flag disagrees with the version, a commit that is not the release's or one of its descendants, a commit whose library or build
  settings differ from the release's, and a commit without a successful main-branch run of `ci.yml` and `codeql.yml`. The library check
  (`packaging/Assert-ReleaseCode.ps1`) compares every file under `src/ResonantPulse.Core` (its README and lock file apart), the
  project file and the `Directory.Build.props`, `Directory.Build.targets` and `Directory.Packages.props` files above it as parsed XML
  (comments and layout do not count, and neither does anything that only describes the package or the version: description, tags, URLs,
  copyright, license, authors, the packed README, the version; a property, item, condition, target or import counts however it is
  written), and `Directory.Build.rsp`, `NuGet.config`, `global.json`, `.editorconfig` and `.globalconfig`. A project that depends on
  something the check does not follow or evaluate is refused until the check is taught to read it: an import, a target or task, a project
  reference, a path outside its own folder (also a rooted one, in a value or in a condition), a condition on the file system, a property
  function, an escaped character, `TreatAsLocalProperty` (which lets a project override the version that the package is made with), a
  read of one of the package properties that the comparison leaves out, or a read of anything that the pipeline does not fix: only
  `$(Version)` and `$(Configuration)` (given on the command line) and `$(NoWarn)` inside the `NoWarn` property itself (which adds to
  the default) may be read. An environment variable, the operating system, a reserved property, an item list and item metadata may
  not, and neither may a property that the project defines itself: its value can come from the environment (`<P>$(P)</P>`), from
  itself or from a cycle, or write a path one piece at a time. Only a plain `$(Name)` is understood, so a method called on a property,
  `%(Metadata)` and `@(Items)` are refused whatever their names. So are a package reference (the library has no package
  dependencies, and a package that restore resolves is code that is not among the compared files), a name or a wildcard that could
  take in a file that the comparison leaves out (a README, the lock file), a CDATA section, text beside markup (a child element, a
  processing instruction), and a symbolic link or a submodule among the
  library's files. Text that a comment splits is read joined, as MSBuild joins it. When the candidate is
  the release's own commit
  (as it is when `release.yml` calls the workflow) nothing is compared. The package itself is made by
  the candidate's own `Build-NuGetPackage.ps1`, which gives the version and the configuration and nothing else; that script is part of
  the reviewed pipeline and is not compared (the tagged commit does not have it).
  `packaging/Assert-ReleaseCode.Tests.ps1` runs the check on changes to a copy of the real project in the "Musical correctness" job of
  every build. The workflow then runs the engine's tests, packs and checks the package, and hands it to a second job, the only one with
  `packages: write`, which has no checkout and runs no repository code. A version that is already on GitHub Packages is skipped, never
  overwritten.
- Visibility: the first version, 1.0.0, came out public when `package.yml` pushed it on 3 November 2026 (a package pushed from a
  workflow of a public repository takes the repository's visibility; the page
  <https://github.com/tinodo/Resonant-Pulse/pkgs/nuget/ResonantPulse.Core> answers 200 to an anonymous request). The tuner's first version
  came out private, so the summary of the run says whether the package is public. If one ever is not: GitHub has no API to change it, so
  open the package (<https://github.com/tinodo?tab=packages&repo_name=Resonant-Pulse>), Package settings, Danger Zone, Change visibility,
  Public. That cannot be undone, so it is the owner's decision. Restoring any NuGet package from GitHub Packages, a public one too,
  needs a personal access token with `read:packages` ([README](../README.md#using-resonantpulsecore)).

## Website

<https://tinodo.github.io/Resonant-Pulse/> is served by GitHub Pages with the build type "GitHub Actions" (Settings, Pages).
`pages.yml` builds `site/` with `packaging/Build-Site.ps1` and deploys it from main when `site/`, `docs/PRIVACY.md`, the script and its
tests, the screenshots or the workflow change, every Monday, and on request. A pull request only builds and checks; the result is kept for seven
days as the artifact `website-preview`.

- The pictures of the application on the page are the English screenshots in `packaging/store-assets/screenshots/en-US`, copied in at
  build time so that there is one copy of them. The icon and the preview image of the site are made from the Store artwork by
  `packaging/Generate-SiteAssets.ps1`.
- The build fails when a link, a picture, a style-sheet `url()` or `@import`, or an entry of the sitemap or `robots.txt` points at nothing or
  outside the site, a canonical or preview address is not this site's, a page has no language, title, description or alt text, an
  attribute that the checks read is not written `name="value"` (single quotes, no quotes, spaces around the equals sign, or no space
  before the attribute are refused) or is given twice (a browser keeps the first), a fragment is not in the letter case of its id (`#top`
  needs none), a style sheet, style block or style attribute has
  something that the check does not read (`image-set()`, `src()`, an escape outside a string or in an address), a link is not https, text
  is unfinished, or `site/privacy.html`
  and `docs/PRIVACY.md` differ in their effective date or in a word (formatting and the last section, which says where the policy is
  published, are left out of the comparison). The privacy policy has those two copies and they must say the same: change both in one pull
  request. `packaging/Build-Site.Tests.ps1` proves the check on each of those defects (and that the site as it is passes, in both Store
  states); `pages.yml` runs it before the build.
- The Microsoft Store block of the home page follows `STORE_FIRST_PUBLICATION_COMPLETED`: `false` shows "coming soon", `true` shows the
  Store badge and a link to `STORE_PRODUCT_ID`. After the variable is changed, run `pages.yml` (or wait for Monday).
- After a deployment the job requests the live site: the home page, the privacy policy, the stylesheet, six pictures and a 404 page.
  An accepted deployment is not yet a website that answers.
- `https://tinodo.github.io/Resonant-Pulse/privacy.html` is the privacy policy URL of the Store listing.

## Microsoft Store prerequisites

No Store identity or authorization is guessed from the separate Resonant tuner.
Reserve this product in Partner Center and obtain the exact public identity
values. Configure repository variables:

| Variable | Exact value required |
|---|---|
| STORE_IDENTITY_NAME | Package/Identity/Name |
| STORE_IDENTITY_PUBLISHER | Package/Identity/Publisher |
| STORE_PUBLISHER_DISPLAY_NAME | Package/Properties/PublisherDisplayName |
| STORE_PRODUCT_ID | Store product ID |
| STORE_FIRST_PUBLICATION_COMPLETED | `true` only after first publication is live |
| STORE_IS_FREE | `true` only if the product is actually free |

Run the Microsoft Store workflow with `submit_update=false` to get the real
identity-bearing candidate. The workflow also exercises installed activation
with that exact product identity on a disposable runner. Review/install-test
the package on physical supported Windows devices, run applicable
Windows App Certification Kit checks and complete the first submission through
Partner Center: listing, genuine screenshots, age rating, markets, pricing,
privacy/support URLs, capability declarations and certification.

Microsoft's current CLI cannot create the product or complete its first
publication. It currently supports free-product updates only. Do not change
pricing or tenant associations merely to satisfy automation.

After first publication, configure protected `microsoft-store` environment
secrets: `STORE_TENANT_ID`, `STORE_SELLER_ID`, `STORE_CLIENT_ID`,
`STORE_CLIENT_SECRET`. These come from authorized Partner Center API access.
Never paste a client secret into source, issues, logs or a chat transcript.

An update submission authenticates, uploads and polls the provider result.
Unknown delivery/certification is not success. Check Partner Center and the
Store listing before announcing a published version.

Store MSIX distribution does not require buying a public signing certificate;
Store signing and local sideload trust are separate. The unsigned development
MSIX is a packaging diagnostic, not an installable signed release. No build
workflow installs a certificate into the developer machine.

## Sources checked 25 October 2026

- https://learn.microsoft.com/en-us/windows/apps/publish/msstore-dev-cli/overview
- https://learn.microsoft.com/en-us/windows/apps/publish/msstore-dev-cli/commands
- https://learn.microsoft.com/en-us/windows/apps/publish/msstore-dev-cli/github-actions
- https://docs.github.com/en/copilot/how-tos/use-copilot-agents/use-code-review
- https://docs.github.com/en/copilot/how-tos/copilot-on-github/set-up-copilot/configure-code-review
- https://docs.github.com/en/rest/repos/rules#create-a-repository-ruleset
- https://learn.microsoft.com/en-us/windows/msix/package/create-certificate-package-signing
- https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iapplicationactivationmanager-activateapplication
- https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getpackagefullname

## Release acceptance beyond compilation

Measure audio onset/rate/visual synchronization, pause/resume/device loss,
minimized/battery behavior, silence and count-in semantics on physical devices.
Test install/update/uninstall on clean Windows; verify all five languages,
keyboard/Narrator, Contrast/reduced motion and high DPI. Review every capability
in DELIVERY.md. No marketing claim of "best", microsecond timing or universal
hardware support follows from synthetic tests.
