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

## Dutch terms to check first

These are the choices most likely to be wrong. They are questions, not findings:
none has been checked with a Dutch musician.

- **Laag** for a lane (a layer of clicks with its own rhythm and sound). A
  recording program might call it a track.
- **Telwaarde** for the bottom number of a time signature. Notenwaarde is the
  other word that comes to mind.
- **Aftelmaten** for the bars counted in before the music starts.
- **Tik** for one click of the metronome (*Eigen tikposities*, *hier geen
  nieuwe tik*).
- **Gemiddeld** for the middle strength, which English calls *Medium*.
- **Tempocurve** for the way the tempo changes over a program step.
- **Gebonden** for a click that is tied to the one before it.
