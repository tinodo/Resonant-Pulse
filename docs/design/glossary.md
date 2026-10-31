# Glossary: musical terms in the five languages

Resonant Pulse shows its text in English, Dutch, German, French and Spanish. The
text of the new and rewritten pages (Timer, Project, Help, Settings, Library,
Stage, Train and Studio) in the last four languages was written with GitHub
Copilot, and **no native-speaking musician has reviewed it yet**. This table
lists the terms a musician will notice first, as the app shows them, so that a
review can start there.

To change a term, edit its line in `src/ResonantPulse.App/Localization/Text.cs`.
Each line is `Add("Key", English, Dutch, German, French, Spanish)`; the tests check
that every key exists in all five languages and that the placeholders (`{0}`)
match, so a translation cannot be left out or lose one.

| Key | English | Dutch | German | French | Spanish |
|---|---|---|---|---|---|
| `TimeSignature` | Time signature | Maatsoort | Taktart | Mesure | Compás |
| `Bars` | Bars | Maten | Takte | Mesures | Compases |
| `Beats` | Beats per bar | Tellen per maat | Zählzeiten pro Takt | Temps par mesure | Tiempos por compás |
| `BeatNote` | Beat note (bottom number) | Telwaarde (onderste getal) | Notenwert (untere Zahl) | Valeur du temps (chiffre du bas) | Figura del pulso (número inferior) |
| `Repeats` | Repeats | Herhalingen | Wiederholungen | Répétitions | Repeticiones |
| `Ramp` | Tempo curve | Tempocurve | Tempokurve | Courbe de tempo | Curva de tempo |
| `Subdivision` | Divisions per tempo pulse | Verdelingen per tempopuls | Teilungen pro Tempopuls | Divisions par pulsation de tempo | Divisiones por pulso de tempo |
| `Swing` | Swing · first share (0.5 = straight) | Swing · eerste deel (0,5 = recht) | Swing · erster Anteil (0,5 = gerade) | Swing · première part (0,5 = droit) | Swing · primera parte (0,5 = recto) |
| `Groups` | Additive groups (e.g. 2+2+3; empty = none) | Additieve groepen (bv. 2+2+3; leeg = geen) | Additive Gruppen (z. B. 2+2+3; leer = keine) | Groupes additifs (ex. 2+2+3 ; vide = aucun) | Grupos aditivos (p. ej. 2+2+3; vacío = ninguno) |
| `CountIn` | Count-in bars | Aftelmaten | Einzähltakte | Mesures de décompte | Compases de entrada |
| `Tap` | Tap tempo | Tempo tikken | Tempo tippen | Taper le tempo | Marcar tempo |
| `TapStart` | Tap to start | Start met tikken | Durch Tippen starten | Démarrer en tapant | Marcar para iniciar |
| `TapStartCountField` | Taps to start | Tikken om te starten | Zum Start tippen | Frappes pour démarrer | Toques para iniciar |
| `UnitTaps` | taps | tikken | Mal | frappes | toques |
| `TapStartCount` | {0} of {1} taps | {0} van {1} tikken | {0}/{1} getippt | {0}/{1} frappes | {0} de {1} toques |
| `TapStartRejected` | Tap again | Tik opnieuw | Nochmal tippen | Tapez encore | Marque otra vez |
| `LevelStrong` | Strong | Sterk | Stark | Fort | Fuerte |
| `LevelNormal` | Medium | Gemiddeld | Mittel | Moyen | Medio |
| `LevelSoft` | Soft | Zacht | Leise | Doux | Suave |
| `LevelSilent` | Silent | Stil | Stumm | Silence | Silencio |
| `Lane` | Lane | Laag | Spur | Piste | Pista |
| `AddLane` | Add lane | Laag toevoegen | Spur hinzufügen | Ajouter une piste | Añadir pista |
| `Mute` | Mute | Dempen | Stummschalten | Couper le son | Silenciar |
| `Solo` | Solo lane | Laag solo | Spur solo | Piste solo | Pista en solo |
| `Rotate` | Rotate pattern | Patroon roteren | Muster drehen | Faire tourner le motif | Rotar patrón |
| `Invert` | Invert rests | Rusten omkeren | Pausen umkehren | Inverser les silences | Invertir silencios |
| `Euclidean` | Euclidean rhythm | Euclidisch ritme | Euklidischer Rhythmus | Rythme euclidien | Ritmo euclidiano |
| `Volume` | Volume | Volume | Lautstärke | Volume | Volumen |
| `Pan` | Pan (−1 left, +1 right) | Pan (−1 links, +1 rechts) | Pan (−1 links, +1 rechts) | Panoramique (−1 gauche, +1 droite) | Panorama (−1 izquierda, +1 derecha) |
| `CustomPositions` | Custom beat positions | Eigen tikposities | Eigene Zählzeitpositionen | Positions personnalisées | Posiciones personalizadas |
| `PositionField` | Position | Positie | Position | Position | Posición |
| `UnitQuarterNotes` | quarter notes | kwartnoten | Viertelnoten | noires | negras |
| `PositionStrength` | Strength | Sterkte | Stärke | Force | Fuerza |
| `PositionChance` | Chance this click plays | Kans dat deze tik klinkt | Wahrscheinlichkeit, dass dieser Klick erklingt | Probabilité que ce clic sonne | Probabilidad de que suene este clic |
| `PositionTie` | Tied: no new click here | Gebonden: hier geen nieuwe tik | Gebunden: hier kein neuer Klick | Liée : pas de nouveau clic ici | Ligado: sin clic nuevo aquí |
| `Step` | Step | Stap | Schritt | Étape | Paso |
| `AddStep` | Add program step | Programmastap toevoegen | Programmschritt hinzufügen | Ajouter une étape | Añadir paso |
| `StepGapSwitch` | Leave some bars silent | Sommige maten stil laten | Einige Takte stumm lassen | Laisser certaines mesures silencieuses | Dejar algunos compases en silencio |
| `TrainProgramTitle` | Practice program | Oefenprogramma | Übungsprogramm | Programme d’entraînement | Programa de práctica |
| `Setlist` | Setlist | Setlijst | Setliste | Liste de scène | Repertorio |
| `NextCountIn` | Next + count-in | Volgende + aftellen | Weiter + Einzählen | Suivant + décompte | Siguiente + entrada |
| `ReducedMotion` | Reduce motion | Minder beweging | Bewegung reduzieren | Réduire les mouvements | Reducir el movimiento |
| `HelpQuickStart` | Quick start | Snel starten | Schnellstart | Prise en main | Inicio rápido |
| `HelpReference` | Musical reference | Muzikale uitleg | Musikalische Grundlagen | Repères musicaux | Referencia musical |
| `HelpBpmTitle` | BPM and the note value | BPM en de nootwaarde | BPM und Notenwert | BPM et valeur de note | BPM y figura de la nota |
| `HelpMeterTitle` | Time signature and the click | Maatsoort en de tik | Taktart und Klick | Mesure et clic | Compás y clic |
| `HelpFeelTitle` | Subdivision and swing | Onderverdeling en swing | Unterteilung und Swing | Subdivision et swing | Subdivisión y swing |
| `TempoGuideValue` | Approximate markings: {0} | Globale aanduidingen: {0} | Ungefähre Bezeichnungen: {0} | Indications approximatives : {0} | Indicaciones aproximadas: {0} |
| `TempoGuideConverted` | Approximate markings at {0} BPM in {1}: {2} | Globale aanduidingen bij {0} BPM in {1}: {2} | Ungefähre Bezeichnungen bei {0} BPM in {1}: {2} | Indications approximatives à {0} BPM en {1} : {2} | Indicaciones aproximadas a {0} BPM en {1}: {2} |
| `TempoGuideInput` | Enter a tempo from 1 to 1000 BPM to see its markings. | Typ een tempo van 1 tot 1000 BPM om de aanduidingen te zien. | Tempo von 1 bis 1000 BPM eingeben, um die Bezeichnungen zu sehen. | Saisissez un tempo de 1 à 1000 BPM pour voir les indications. | Escriba un tempo de 1 a 1000 BPM para ver las indicaciones. |
| `ExportTimingCsv` | Export result (CSV) | Resultaat exporteren (CSV) | Ergebnis exportieren (CSV) | Exporter le résultat (CSV) | Exportar resultado (CSV) |
| `TimingCsvReady` | Ready to export: {0}, rhythm {1}, finished {2}. | Klaar om te exporteren: {0}, ritme {1}, afgerond {2}. | Bereit zum Exportieren: {0}, Rhythmus {1}, beendet {2}. | Prêt à exporter : {0}, rythme {1}, terminé {2}. | Listo para exportar: {0}, ritmo {1}, finalizado {2}. |
| `TimingCsvUnavailable` | There is no finished check to export. Finish a check first. | Er is geen afgeronde check om te exporteren. Rond eerst een check af. | Es gibt keine beendete Prüfung zum Exportieren. Beenden Sie zuerst eine Prüfung. | Aucun contrôle terminé à exporter. Terminez d’abord un contrôle. | No hay ninguna comprobación terminada que exportar. Termine primero una comprobación. |
| `TimingNoName` | (no name) | (zonder naam) | (ohne Namen) | (sans nom) | (sin nombre) |
| `TimingDriftUnavailable` | Drift needs at least four matched presses on clicks at different times. | Drift vraagt minstens vier gekoppelde toetsdrukken op tikken op verschillende momenten. | Drift braucht mindestens vier zugeordnete Tastendrücke auf Klicks zu unterschiedlichen Zeitpunkten. | La dérive nécessite au moins quatre pressions associées à des clics à des moments différents. | La deriva necesita al menos cuatro pulsaciones emparejadas con clics en momentos distintos. |
| `TimingCsvHint` | Saves the last finished check as a CSV file for a spreadsheet: the totals, then each click and each press. The result stays available until you start another check or close the app. Column names and numbers are the same in every language. | Bewaart de laatst afgeronde check als CSV-bestand voor een spreadsheet: de totalen, daarna elke tik en elke toetsdruk. Het resultaat blijft beschikbaar tot u een nieuwe check start of de app sluit. Kolomnamen en getallen zijn in elke taal hetzelfde. | Speichert die zuletzt beendete Prüfung als CSV-Datei für eine Tabellenkalkulation: die Summen, dann jeden Klick und jeden Tastendruck. Das Ergebnis bleibt verfügbar, bis Sie eine neue Prüfung starten oder die App schließen. Spaltennamen und Zahlen sind in jeder Sprache gleich. | Enregistre le dernier contrôle terminé dans un fichier CSV pour un tableur : les totaux, puis chaque clic et chaque pression. Le résultat reste disponible jusqu’à ce que vous lanciez un nouveau contrôle ou fermiez l’application. Les noms de colonnes et les nombres sont les mêmes dans toutes les langues. | Guarda la última comprobación terminada como archivo CSV para una hoja de cálculo: los totales y después cada clic y cada pulsación. El resultado sigue disponible hasta que inicie otra comprobación o cierre la aplicación. Los nombres de columna y los números son iguales en todos los idiomas. |

## Dutch terms to check first

These are the choices most likely to be wrong. They are questions, not findings:
none has been checked with a Dutch musician.

- **Laag** for a lane (a layer of clicks with its own rhythm and sound). A
  recording program might call it a track.
- **Telwaarde** for the bottom number of a time signature. Notenwaarde is the
  other word that comes to mind.
- **Aftelmaten** for the bars counted in before the music starts.
- **Tik** for one click of the metronome (*Eigen tikposities*, *hier geen
  nieuwe tik*). *Tik* is also what a tap is called (*Tempo tikken*, *Start met
  tikken*, *3 van 4 tikken*), so one word means two things in the same window.
- **Gemiddeld** for the middle strength, which English calls *Medium*.
- **Tempocurve** for the way the tempo changes over a program step.
- **Gebonden** for a click that is tied to the one before it.
- **Start met tikken** for the *Tap to start* switch, and **tikken** for the number of taps.

## Tap to start in German, French and Spanish

Also questions, not findings; none has been checked with a native speaker.

- German: **Durch Tippen starten** for the switch, **Mal** as the unit after the
  number of taps (*Zum Start tippen: 4 Mal*), and *3/4 getippt* for the count.
  *Tipps* was avoided on purpose, because it reads as "tips".
- French: **Démarrer en tapant** for the switch, counted in **frappes**.
- Spanish: **Marcar para iniciar** for the switch (*marcar* is also the verb of
  *Marcar tempo*), counted in **toques**.
- The sentence after a refused start (*Switch Tap to start on again and tap a
  different tempo*, key `TapStartRange`) names the switch with its label, unquoted,
  as the help for the number of taps does: *Zet Start met tikken weer aan*,
  *Schalten Sie Durch Tippen starten wieder ein*, *Réactivez Démarrer en tapant*,
  *Active de nuevo Marcar para iniciar*. Whether a label inside a sentence reads
  better in quotation marks is a question for a native speaker.

## The Help panel: quick start and musical reference

Also questions, not findings; none of this has been checked with a native speaker.
Every sentence of the Help names a control with the label that control has in that
language: the label is inserted from the control's own string, so a label changed
in `Text.cs` changes the Help with it. The few names that are written into a sentence
(the Tempo panel, and the first words of the Subdivision and Swing labels) are pinned
to their labels by a test, which fails if they stop matching.

- Dutch: **Snel starten** and **Muzikale uitleg** for the two headings;
  **nootwaarde** for the note that BPM counts (the Tempo panel says *Tempo-nootwaarde*,
  while *Telwaarde* above is the bottom number of a time signature, which the Help
  explains as *welke noot één tel is*); **onderverdeling** for a subdivision (the
  field says *Onderverdeling: tikken tussen de tellen*); **lang-kort** for the swung
  pair; **tel** (a beat), **tik** (a click) and **maat** (a bar) kept apart. The
  Help speaks to the reader as *u*.
- German: **Schnellstart** and **Musikalische Grundlagen**; **Zählzeit** for a beat
  with **Betonung** for its strength (the strip is called *Zählzeit-Betonung*);
  **Unterteilung** for the subdivision; **Lang-kurz** for the swung pair. The Help
  uses *Sie*.
- French: **Prise en main** for the quick start (*Démarrage rapide* is the other
  choice) and **Repères musicaux**; **temps** for a beat; **force** for its
  strength; **noire pointée** and **croches swinguées**; **Arrêter** and **Échap**
  as the names of the Stop button and the Esc key. The Help uses *vous*.
- Spanish: **Inicio rápido** and **Referencia musical**; **pulso** for a beat and
  **intensidad** for its strength; **figura** for the note that BPM counts (the Tempo
  panel says *Unidad de tempo*); **negra con puntillo** and **corcheas con swing**;
  **entrada** for the count-in. The Help uses *usted*.

## The tempo guide: Italian terms and the sentences around them

Also questions, not findings; none of this has been checked with a native speaker.
The eleven Italian terms (Larghissimo, Grave, Largo, Larghetto, Adagio, Andante,
Moderato, Allegro, Vivace, Presto, Prestissimo) are written the same way in every
language, so they are not entries of `Text.cs`: they are `TempoGuide.Names` in code, and
a test pins them. The tests that ask every string to exist in five languages and to
differ from English never see them, which is what an Italian term must not do. Only
the sentences around them are translated, and the word for "markings" is the choice to
check first:

- Dutch: **aanduidingen** (*Globale aanduidingen*). *Tempoaanduidingen* and
  *tempobenamingen* are the compounds that come to mind.
- German: **Bezeichnungen** (*Ungefähre Bezeichnungen*); *Tempobezeichnungen* is the
  longer compound.
- French: **indications** (*Indications approximatives*), as in *indications de
  tempo*; the sentence for another note value names the note with the app's own word
  for quarter notes (*à 60 BPM en noires*).
- Spanish: **indicaciones** (*Indicaciones aproximadas*); *términos de tempo* is the
  other choice.
- French sets a colon off with a space, and the app's other French strings use an
  ordinary one. These sentences use a no-break space, so that a line that wraps never
  starts with the colon. The line wraps in a column about 120 to 200 DIP wide, so a
  no-break space also joins each pair of names (*Moderato / Allegro*) and a number with
  *BPM* in every language.
- *Approximate* is a condition of every sentence, not a courtesy: the speed that suits a
  marking varies with the piece, the period and the performer. A test requires the word
  in all five languages (*Globale*, *Ungefähre*, *approximatives*, *aproximadas*).

## The timing export: terms and the sentences around them

Also questions, not findings; none of this has been checked with a native speaker. The
timing check already called itself a *check* (*Check starten*, *Prüfung starten*, *Démarrer
le contrôle*, *Iniciar comprobación*); the export keeps that word and adds three kinds of
text: the command and its hint, the line that says what an export would hold, and the
sentence that says why a drift was not worked out.

- The words that are **not** translated, on purpose: the file itself. Its column names
  (`record_type`, `observed_seconds`, ...), its row names, its descriptions and the name of
  the main beat in it (`Main beat`) are English and the same in every language, because a
  script or a spreadsheet formula must read every file the same way. They are not entries
  of `Text.cs`, so the tests that ask for five languages never see them; the hint says so
  (*Kolomnamen en getallen zijn in elke taal hetzelfde*, *Spaltennamen und Zahlen sind in
  jeder Sprache gleich*, *Les noms de colonnes et les nombres sont les mêmes dans toutes
  les langues*, *Los nombres de columna y los números son iguales en todos los idiomas*).
  The page names the main beat in the language in use (*Hoofdpuls*, *Hauptpuls*, *Pulsation
  principale*, *Pulso principal*); the file does not.
- **CSV** is not translated. The Dutch hint says **spreadsheet** (the usual loanword, and
  the word the Dutch Windows apps use); German says **Tabellenkalkulation**, French
  **tableur** and Spanish **hoja de cálculo**.
- A **key press** is *toetsdruk* (Dutch), *Tastendruck* (German), *pression* (French) and
  *pulsación* (Spanish): the app's earlier timing text said *toetsdrukken* and
  *Tastendrücke* already. French *pression* and the click's *clic* are the pair to check.
- **Drift** is *drift* in Dutch (a loanword; *afwijking in het tempo* is the other choice),
  *Drift* in German, *dérive* in French and *deriva* in Spanish. The sentence now says what
  drift needs (at least four matched presses on clicks at different times) instead of
  "unavailable", because a blank drift in the file and on the page is not a drift of 0.
- *Ready to export: {0}, rhythm {1}, finished {2}* names three things, in this order: the
  project, the rhythm (a lane's name or the main beat) and when the check ended, in the
  language's own date and time format. **Ritme** (Dutch), **Rhythmus** (German), **rythme**
  (French) and **ritmo** (Spanish) are the words to check against the lane and main-beat
  names that the same page already uses. A project or a lane without a name (the app allows
  an empty name) stands in the line as *(no name)*: *(zonder naam)*, *(ohne Namen)*, *(sans nom)*
  and *(sin nombre)*; the file keeps it empty.
- In French the colon has a no-break space before it (*tableur :*, *exporter :*, as in the tempo guide's
  *Indications approximatives :*), so that a narrow card never begins a line with the colon.
- The hint is longer than the app's other hints on purpose: it says what the file is, how
  long the result stays (until another check starts or the app closes: nothing is kept
  from one run of the app to the next) and that the column names are not translated. A
  claim keeps its conditions, so it was not shortened.
- *Afgerond* (Dutch, for finished) is the word the Finish command's own text uses (*Afronden
  en resultaat tonen*); German uses **beendet** and **Beenden**, French **terminé** and
  **Terminer**, Spanish **terminada**/**finalizado**: Spanish uses two words (*Terminar* on
  the command, *finalizado* in the ready line) and a reviewer may prefer one.

## The separate tracks export: terms and the sentences around them

Questions again, not findings; nothing here has been checked with a native speaker. The word
**stem** is not used anywhere in the interface (it is jargon to someone who has never mixed a
song, and the language rule of the page design asks for plain words): the command says what a
person gets, *separate tracks*, and the file type in brackets, as *Export result (CSV)* does.

- **Track** and **lane** are two words in English and in Dutch (a lane is a *laag*, a track
  is a *spoor*), but in German, French and Spanish the word the app already uses for a lane
  (*Spur*, *piste*, *pista*, see `Lane` above) is also the usual word for a track in music
  software: *Einzelspuren exportieren (ZIP)*, *Exporter les pistes séparées (ZIP)*, *Exportar
  pistas separadas (ZIP)*. The question counts the main beat as a track, so a project with two
  lanes says three (*Spuren: 3*, *Pistes : 3*, *Pistas: 3*, *Sporen: 3*), which a reader may
  count against the two lanes of Studio. If that reads badly, the count can be of **files**
  (*Dateien*, *fichiers*, *archivos*) in those three languages; the sentence before it already
  says *each lane* and *the main beat*.
- **ZIP** and **WAV** are not translated (the file types, as **CSV** is not). The articles that
  go with them are the questions: *one ZIP* is *één ZIP* (Dutch, *de*), *eine ZIP-Datei*
  (German, a compound), *un ZIP* (French, masculine) and *un solo ZIP* (Spanish, masculine);
  *WAV-bestand*, *WAV-Datei*, *fichier WAV* and *archivo WAV* are the file.
- **Music software** is the phrase the Export card already uses (*other music software*):
  *muzieksoftware*, *Musiksoftware*, *logiciel de musique* and *software musical*; the hint
  uses the same words so that one thing has one name on the page.
- **The count-in** is *het aftellen* (Dutch), *das Einzählen*, *le décompte* and *la cuenta de
  entrada*, the words of the WAV sentence next to it (*inclusief het aftellen*, *einschließlich
  Einzählen*, *décompte compris*, *incluida la cuenta de entrada*), not the *aftelmaten* of the
  Project panel's field. A **muted** lane is *gedempt* (*Dempen*), *stummgeschaltet*
  (*Stummschalten*), *dont le son est coupé* (*Couper le son*) and *silenciada* (*Silenciar*),
  the words of the lane's own Mute switch; a silent file is *een stil bestand*, *eine stille
  Datei*, *un fichier silencieux* and *un archivo en silencio*.
- **Unzipped** is *uitgepakt*, *entpackt*, *décompressées* and *descomprimidas*. French says
  *décompressées* because *dézippées* is slang; the sentence is about the files, so the participle
  agrees with *elles* (the tracks), not with *les fichiers*: worth a native reader's look.
- **Sizes and lengths** are not translated: the units are KiB, MiB and GiB in every language
  (binary units, 1 MiB is 1,048,576 bytes, so no language's *MB* or *Mo* can mean something
  else), with the language's own decimal separator (*6,2 MiB*) and a no-break space between the
  number and its unit (so that a narrow dialog never ends a line between them; a test fails on an
  ordinary space there), and a length is *m:ss* (*0:34*) with the same digits everywhere.
- French keeps the no-break space before *?*, *:* and *;* in the question (*ZIP ?*, *Pistes :*,
  *{2} ;*), as the tempo guide and the timing hint do, so a narrow dialog never starts a line with
  them; a test fails on an ordinary space there.
- *This can take a while.* (*Dit kan even duren.*, *Das kann etwas dauern.*, *Cela peut prendre un
  moment.*, *Esto puede tardar un poco.*) is added to the question only from 512 MiB unzipped
  (half a GiB); below it the question does not say it. docs/WAV-STEMS.md has the measured times.
- The sentence for a track that would be longer than a WAV file can be (4 GiB, about 6 hours)
  names the **setting that can be changed** with the label that setting has in the language
  (*Export length*, *Exportlengte*, *Exportlänge*, *Durée d’export*, *Duración de exportación*):
  a test takes the label from the setting's own string.
- The sentence for a file that could not be saved says what to check (the location, the free
  space, another program that has the file open) and that **a file that was already there has
  not been changed**, which is true because the archive is written next to it and moved over it
  only when it is whole.
