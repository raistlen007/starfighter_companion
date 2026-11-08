# Pilot file formats

All binary formats are little-endian, packed (no padding beyond what is listed), and described in
`src/StarfighterCompanion.Core/Formats`. Bytes whose meaning is unknown, or which only hold the game's
own scratch state (menu selections, last-mission results, in-progress sequences), are not shown and are
always written back unchanged.

## X-Wing — `.plt`

* Source: OpenXW `src/xw/frontend/register.h`, `src/xw_runtime/storage/pilot_file.c`.
* Windows 1998 record: **1,705 bytes**. DOS (1993 floppy, 1994 CD-ROM) record: **1,706 bytes** — it reserves
  87 bytes at 0x289 instead of 86, so everything from the tour status onwards is one byte later.
* The game may write the record twice in a row (3,410 / 3,412 bytes); readers use the first copy.
  When an edited file is saved, every copy is refreshed from the first.
* The pilot's name is the file name; it is not stored inside.
* Rules: one active tour at most, and it must be the current tour (error); hits ≤ shots, per-craft kills ≤ total (warnings).

## TIE Fighter — `.tfr`

* Source: OpenTIE `src/tie/shipext.h`, `src/tie_runtime/storage/pilot_storage.c`.
* Record: **1,928 bytes**, stored twice (current + backup) = 3,856 bytes. Same layout in all editions.
* Values: rank 0–5, difficulty 0–2, Secret Order rank 0–9, battle status 0–3, current battle 0–19.
* The pilot's name is the file name.

## X-Wing vs. TIE Fighter — `.plt`

* Source: OpenXvT `PilotXvtRecord` in `src/xvt/frontend/pilot.c`.
* Record: **253,754 bytes** (0x3DF3A). Statistics cover 88 craft types.
* The name (14 bytes) is read-only: the game matches pilot files by it.
* The rating name is updated automatically when the rating changes.

## Balance of Power — `.pl2`

* Source: OpenXvT `PilotData` in `src/xvt/frontend/pilot_record.h`.
* Record: **296,238 bytes**. Statistics cover 100 craft types; adds campaigns.
* BoP keeps `<name>0.pl2` and an XvT-compatible `<name>0.plt` side by side.

## X-Wing Alliance (XWAUP, TFTC) — `.plt`

* Source: OpenXWA `PilotData` in `src/xwa/frontend/mission_setup.h`.
* Record: **152,076 bytes**. Statistics cover 512 craft types (listed by number, since XWAUP and TFTC change the craft list).
* Values: rating 0–24 (the per-rating mission table has 25 entries), rank 0–8, Kalidor Crescent 0–6, mission skips 0–3.

## XWVM — `.vmpilot`

* Source: XWVM `Assets/Code/GameState/*.cs`.
* XML: `<PilotRecord Name>` with one `<PilotGameRecord Id="xwing|tf|xvt|xwa">` per game, containing tour, historic,
  training, award, kill and statistic records.
* The document is edited in place, so unknown elements and attributes survive.
* Rules: the active tour must have a tour record (XWVM fails to load the pilot otherwise); a tour's
  `CurrentTourStep` of 2147483647 means completed.

## Adding a format

1. Describe the record with `LayoutBuilder` (skip every byte you do not want to expose).
   `Build(size)` throws if the layout does not add up to the expected size.
2. Subclass `BinaryPilotDocument` and put cross-field checks in `CheckRules()`. Use `CommonRules.Broken` for
   problems that would break the game and `CommonRules.Odd` for warnings.
3. Register the format in `PilotFormats.All` and add tests.
