# Timing check CSV (schema version 1)

**Export result (CSV)** in the Train page's timing check saves one finished check as a CSV file, for a spreadsheet or a script.
This page says exactly what is in the file and what it depends on. The same format is written in every language of the app, on every
computer.

## What it is, and what it is not

- **One check.** It is the last check that finished. The app keeps it in memory until you start another check or close the app. Nothing
  is saved on its own and nothing is kept from one run of the app to the next: it is not a practice history (`docs/CAPABILITIES.md` still
  lists a CSV history as not delivered).
- **Input arrival.** `observed_seconds` are the times at which F (or the Hit button) reached the app, counted from the start of the check
  with the system's timer, and `expected_seconds` are the times of the clicks that the check followed. Keyboard, system, display, speaker
  and Bluetooth delays are not corrected for, nothing is recorded or heard, and a mean offset is not subtracted ("tap in time" is not a
  calibration). `source` says `keyboard-ui-arrival`, and `limitation` says it again, in the file.
- **Not acoustic.** `start_frame` and `end_frame` are positions in the audio stream as the app had processed it. That is not what the
  speakers played at that moment.

## What it holds when the project changes afterwards

The check keeps what it measured: the project's name, the rhythm's name and place, the sample rate, the frames, the tolerance, whether
unplayed beats counted, the times of the clicks and of the presses, and the analysis. It is a copy, so none of this can change after the
check ended:

- **The project is edited or renamed**, or another project is opened: the file still names the project and the rhythm as they were when
  the check ended, and the page's line "Ready to export: ..." says the same.
- **The lane that was followed is removed or renamed**: the file keeps its name, and `target_lane` is the place it had in the project that
  was playing (0 is the first lane), which need not be its place in the project now. The main beat is `-1` and is named `Main beat` in the
  file in every language (the page names it in the language in use).
- **A new check is started**: the old result is gone from that moment, also if the new check never finishes.
- **The check is ended by a change** (Stop, Pause or Start by hand, a live change that makes the metronome play something else, a new
  project, a change of output, sleep, Tap to start, the audio failing): there is no result, and nothing to export.
- **The language is changed**: the result stays, and the page shows it again in the new language. The file does not change with the
  language.

## Format

UTF-8 with a byte order mark, one record per line, CRLF after every record, every field in double quotes (a double quote inside a field is
written twice), a comma between fields. Numbers have a decimal point and no thousands separator, and carry every digit that is needed to
read them back exactly (for example `0.30000000000000004` and `1E-07`). A value that is not available is an empty field, never a zero.
Column and row names, and every description, are English and the same in every language.

Eight columns: `record_type`, `expected_index`, `observed_index`, `expected_seconds`, `observed_seconds`, `offset_milliseconds`, `name`,
`value`. The first record is these names. Then:

| `record_type` | Rows | Columns used |
|---|---|---|
| `metadata` | 17, always, in this order: `schema_version` (1), `source`, `limitation`, `clock_reference`, `index_base` (0), `window_policy`, `completed_at_utc` (ISO 8601, UTC), `project_name_json`, `target_name_json`, `label_encoding`, `target_lane`, `sample_rate`, `start_frame`, `end_frame`, `duration_seconds`, `tolerance_seconds`, `includes_silent_targets` (`true` or `false`) | `name`, `value` |
| `summary` | 10, always, in this order: `expected_count`, `observed_count`, `matched`, `missed`, `extra`, `median_offset_milliseconds`, `spread_milliseconds`, `coverage`, `ambiguous_expected`, `drift_milliseconds_per_second` | `name`, `value` |
| `match` | one for each press that was matched with a click, in time order | the two indices, the two times, `offset_milliseconds` (press minus click, in milliseconds: positive is late) |
| `miss` | one for each click that no press was matched with, by index | `expected_index`, `expected_seconds` |
| `extra` | one for each press that was matched with no click, by index | `observed_index`, `observed_seconds` |

- **Indices** start at 0 and are places in the lists as the check kept them, which is the order in time. A press is matched with one click
  within the tolerance, a click with one press: the match that has the most pairs wins, and among those the one with the smallest total
  distance.
- **Blank values.** `median_offset_milliseconds` and `spread_milliseconds` are blank when nothing matched. `coverage` is blank when the
  check followed no clicks. `drift_milliseconds_per_second` is blank unless there are at least four matches and the first and the last
  half of them are centred on different moments (and the result fits in a number); a blank drift is not a drift of 0, which is a measured
  value.
- **Names.** `project_name_json` and `target_name_json` are JSON strings, so the cell starts with a double quote: a name that starts with
  `=`, `+`, `-` or `@` is not a formula in a spreadsheet, and a name with a quote, a comma or a line break cannot add or move a record or a
  column. Decode each once to get the name exactly. Quotes, backslashes, control characters, DEL, NEL and the line and paragraph
  separators are written as escapes; every other character is written as it is.
- **Window.** The check covers the audio frames from `start_frame` up to, not including, `end_frame`; `duration_seconds` is their
  difference divided by `sample_rate`, at most 600. Clicks and presses outside it are not in the check.

## Writing it

The file is written atomically: to a temporary file next to the destination, then moved over it. If anything goes wrong (the file is open
in another program, it is read-only, the disk is full, the export is cancelled) the file that was there stays as it was, and the error is
shown. A check of more than 100,000 clicks or presses, or of more than four million comparisons (the clicks plus one, times the presses
plus one), cannot be analysed, and then there is no result to export.

## What has not been tried

- The save dialog is Windows' own and cannot be driven on a hosted runner. The file is held by tests that write it to a folder and read it
  with a reader that knows only this format (every field in quotes, CRLF, a byte order mark), with the .NET library's own CSV reader, and
  against exact expected bytes.
- No spreadsheet program has opened a file written by the app. A spreadsheet whose own settings expect a decimal comma may need to be told
  that these numbers have a point.
