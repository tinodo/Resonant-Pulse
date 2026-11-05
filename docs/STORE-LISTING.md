# Store listing preparation

Product name: **Resonant Pulse**. The owner supplied the reserved Partner Center
product identity on 25 October 2026; the exact nonsecret values are recorded in
packaging/StoreIdentity.json and configured as GitHub repository variables.
The listing is live: https://apps.microsoft.com/detail/9nrfh2c8x2ql (docs/DELIVERY.md,
Store first publication). Trademark clearance, commercial terms, markets and the certification
itself were the owner's, in Partner Center, and are not recorded here. Do not claim the entire
design as shipping functionality.

| Field | Prepared value |
|---|---|
| Product name | Resonant Pulse |
| Store product ID | 9NRFH2C8X2QL |
| Store listing | https://apps.microsoft.com/detail/9nrfh2c8x2ql (live; the badge on the website and in the README links to it) |
| Package version | 1.0.0.0 (the fourth part of a Store version is reserved and stays 0), built by the Microsoft Store workflow |
| Category | Music (as listed in the Store) |
| Support | https://github.com/tinodo/Resonant-Pulse/issues |
| Website | https://tinodo.github.io/Resonant-Pulse/ (GitHub Pages; the home page shows the Store badge and a link while `STORE_FIRST_PUBLICATION_COMPLETED` is `true`, as it has been since 5 November 2026, and says "coming soon" otherwise) |
| Privacy | https://tinodo.github.io/Resonant-Pulse/privacy.html (the same text as docs/PRIVACY.md; `pages.yml` fails when their effective dates or words differ) |
| Source/license | https://github.com/tinodo/Resonant-Pulse / MIT |
| Package languages | en-US, nl-NL, de-DE, fr-FR, es-ES |
| Distribution baseline | Windows 11 24H2 or later; x64 and ARM64 builds (the Store package is x64) |
| Account/cloud requirement | None for local metronome functionality |
| Artwork | `packaging/store-assets`: StoreIcon 300x300, Poster 720x1080, BoxArt 1080x1080 and Hero 1920x1080, drawn by `packaging/Generate-StoreArt.ps1` from the application icon's own mark |
| Screenshots | `packaging/store-assets/screenshots/<language>/`: six real captures of the 1.0.0 application (Play, Studio, Train, Stage, Library, Settings), 1920x1080, in each of the five languages, with a README that says how they were made |
| Listing text | Description, short description, features, what's new, search terms and the notes for certification, in the five languages, are kept with the owner's private submission notes, not in this repository |
| Pricing, markets, age rating | Owner/Partner Center decisions, not fabricated defaults |

## Short-description drafts

These describe the intended category, not certification or tested accuracy.
Final listing descriptions must match the actual accepted release.

| Language | Draft |
|---|---|
| English | A native metronome and rhythm workstation for focused practice. |
| Nederlands | Een native metronoom en ritmewerkstation voor gerichte oefening. |
| Deutsch | Ein natives Metronom und eine Rhythmus-Werkstatt zum gezielten Üben. |
| Français | Un métronome natif et un atelier rythmique pour travailler avec précision. |
| Español | Un metrónomo nativo y un taller de ritmo para practicar con atención. |

## Submission evidence

Use screenshots of the **real accepted app**, not static concept SVGs or a
rendered mockup. Capture Play, Studio, Train, Stage, Library and the localized
interface at normal and enlarged text. Do not include secrets, other desktop
windows or development-only warning banners in the listing. The app can render
its own window to a PNG in a build made with `-p:UiTest=true` (see AGENTS.md),
which is how a screenshot is made without a person's desktop in it. The 1.0.0 set (Play, Studio, Train, Stage, Library and Settings, in
each of the five languages) is in `packaging/store-assets/screenshots`; it was captured at 100% text size and in the Dark theme
only, so the enlarged-text and Contrast-theme views named above are not in it.

Reconcile declared capabilities with implemented behavior. The base output-only
package declares full-trust desktop execution, not microphone recording. Adding
input analysis, external device access, network sync, recording or background
services requires a fresh privacy/capability/release review.

Review Partner Center certification messages and the actual live listing after
submission. An uploaded package or a green build is not Store availability.
