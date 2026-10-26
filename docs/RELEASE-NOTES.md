# Resonant Pulse preview

Native WinUI 3/.NET 10 metronome and rhythm workstation under active development.

Help is now a discoverable navigation destination with a five-language quick
start and musical reference. It explains output selection, count-in,
pause/mute/stop, edit boundaries, program tempos, practice limits, note units
versus written beats, subdivisions, swing, accents and tap-in. Visiting Help or
returning to Play never starts playback.

More tempo controls also includes an approximate Italian tempo guide. It follows
the displayed BPM and selected note value, includes overlapping names where
appropriate, and never changes timing or starts playback.

Tap-in is under **More tempo controls**. Choose 2–16 consecutive valid taps,
explicitly arm, then use Tap tempo or T outside input fields. Progress and
rejections are visible. Stop, Escape, Reset, navigation, output change and sleep
cancel intent. Program section tempos scale proportionally, preserving the
selected BPM note unit, count-in and rhythm; out-of-range results are rejected.
Playback starts after saving and audio preparation, not at a calibrated phase.
Arming never survives restart. Normal tap tempo still changes BPM without starting.
Selected-output removal or updates cancel tap-in without needing Refresh.
Reconnection does not rearm it. If endpoint monitoring is unavailable, tap-in
shows retry guidance while normal manual Start remains independent.

The practice timer is in Play's **Practice timer and auto-stop** expander. Set a duration in
seconds and choose Apply; zero disables it. Stop and Start to use a changed
duration. Count-in and pauses do not consume practice time, and a shorter finite
program ends first. The final five milliseconds fade out. This limits live
playback, not export length. Manual timing reports also respect the stop boundary.

Auto-stop also supports written beats and complete bars, excluding count-in.
Use Play's **Practice timer and auto-stop** expander; zero disables an individual
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
