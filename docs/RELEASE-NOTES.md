# Resonant Pulse preview

Native WinUI 3/.NET 10 metronome and rhythm workstation under active development.

The metronome is now the large, centered primary surface on Play, with tempo
inside the cyan/magenta dial. It fills the available performance area rather
than shrinking into a small preview beside a larger numeric input.
**Rhythm**, **Tempo**, **Practice**, **Project** and **Help** open bounded panels.
Precise numeric tempo editing lives in **Tempo**; -1, tap, +1, meter, sound and
pulse strengths stay beneath the instrument. **Audio** on the compact transport
opens output-device and volume controls without occupying the performance area.

The **Sound** selector and individual pulse strengths apply during playback
without restarting the clock. Existing click tails finish in their original
sound. Manual pulse choices override automatic grouping only for edited slots;
unedited group emphasis is preserved across bars, including dotted-note units.
Count-in retains its separate cue accents. Ctrl+M remains available inside
settings panels. Other tempo/rhythm edits still require Stop and Start.

The practice timer is in Play's **Practice** panel. Set a duration in
seconds and choose Apply; zero disables it. Stop and Start to use a changed
duration. Count-in and pauses do not consume practice time, and a shorter finite
program ends first. The final five milliseconds fade out. This limits live
playback, not export length. Manual timing reports also respect the stop boundary.

Auto-stop also supports written beats and complete bars, excluding count-in.
Use Play's **Practice** panel; zero disables an individual
limit. The earliest seconds/beats/bars/program end wins. Mixed meters, tempo
ramps, repeats and silence follow the authored program rather than counting
audible click events.

Download the ZIP for your architecture, extract all files, and read README.md.
The ZIP includes .NET and WinUI runtime components and is not a Store installer.
SHA-256 checksum files accompany each download.

This is a preview, not the completed full product or a hardware-certified timing
instrument. See the repository's docs/DELIVERY.md for current capability evidence,
docs/CAPABILITIES.md for explicit known gaps, and docs/RELEASING.md for
Store-publication prerequisites. No claimed Store availability,
ARM64 hardware result or acoustic accuracy follows from this release.
