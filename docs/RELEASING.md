# Delivery and Store operations

## Build and test

`ci.yml` runs deterministic Core tests and publishes x64/ARM64 Windows builds,
checks the self-contained payload, creates an unsigned development MSIX and
retains TRX results. A passing build is not a passed hardware/audio/Store test.

`release.yml` is manually dispatched from main. It invokes the same complete CI
workflow at that source SHA, then publishes portable ZIPs plus SHA-256 checksums.
Use a preview semantic version until hardware/accessibility acceptance is
established. Failed gates prevent release publication.

Dependabot checks NuGet and GitHub Actions weekly. `copilot-review.yml` requests
the actual `copilot-pull-request-reviewer[bot]` reviewer on PRs, including
Dependabot PRs. It never checks out or executes PR code under
`pull_request_target`. Failure to obtain a review is a visible failed job, not a
fake successful review. If the default Actions token cannot request Copilot,
configure a narrowly scoped `COPILOT_REVIEW_TOKEN` with repository PR access.
Copilot availability/entitlement is account-dependent. A request is not a
completed review or a required human approval.

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
identity-bearing candidate. Review/install-test the package, run applicable
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

## Release acceptance beyond compilation

Measure audio onset/rate/visual synchronization, pause/resume/device loss,
minimized/battery behavior, silence and count-in semantics on physical devices.
Test install/update/uninstall on clean Windows; verify all five languages,
keyboard/Narrator, Contrast/reduced motion and high DPI. Review every capability
in DELIVERY.md. No marketing claim of "best", microsecond timing or universal
hardware support follows from synthetic tests.
