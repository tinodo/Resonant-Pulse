# Store screenshots

Real captures of the 1.0.0 application for the Microsoft Store listing: 1920x1080, six per language, in the order of the listing
(Play, Studio, Train, Stage, Library, Settings), one folder per listing language (`en-US`, `nl-NL`, `de-DE`, `fr-FR`, `es-ES`).

They are not mock-ups. Each one is the application's own window rendered to a PNG by a UI-test build (`-p:UiTest=true`, see
`AGENTS.md`) of the 1.0.0 source, run on a private Windows desktop with its own copy of the saved state, so that nothing of a
person's desktop, projects or files is in them. What the windows show is sample data made for the pictures (a project called
"Polyrhythm practice", a "Tempo ladder" program, a library of five projects and a setlist of four songs, named in each language).
The metronome was started in the capture, at an inaudible volume, so that the pages show it playing; the pages are shown as they are
on screen at 1280x720 DIP (150% display scale), so a long page is cut by the transport bar, as it is on screen.

The interface text of the Dutch, German, French and Spanish pictures is the application's own text in that language, which has had
no native-speaker review. The captions and the listing text are in the owner's private submission notes, not in this repository.
