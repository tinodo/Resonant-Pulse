# Delivery and Store operations

## Build and test

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
Use a preview semantic version until hardware/accessibility acceptance is
established. Failed gates prevent release publication.
Stable releases additionally require `PRODUCT_ACCEPTANCE_COMPLETED=true`,
configured only after full-product/device acceptance is recorded. Every ZIP
contains build-info.json with its source SHA, version, architecture and CI run.

Dependabot checks NuGet and GitHub Actions weekly. The repository's active
`Automatic Copilot review` ruleset requests real reviews for main-targeting PRs,
including new pushes. `copilot-review.yml` verifies that native rule and waits
for the real `copilot-pull-request-reviewer[bot]` review of the exact PR head.
It uses the ordinary `pull_request` event with read-only permissions and never
checks out or executes PR code; no owner token is stored merely to review
Dependabot PRs.
A missing review or timeout fails visibly. Copilot availability/entitlement is
account-dependent. Completion is not approval or an assertion of zero findings;
review conversations must still be resolved before merge.

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
