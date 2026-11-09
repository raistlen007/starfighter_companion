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
* Values: rank 0–5, difficulty 0–2, Secret Order rank 0–9, battle status 0–3. The current battle must exist in the
  edition (1–10 on the 1994 floppy, 1–13 otherwise).
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
* Record: **152,076 bytes**. Statistics cover 512 craft types. Original XWA/OpenXWA labels are bundled from OpenXWA's documented craft-type list; hidden, unused and unmapped IDs remain numeric. XWAUP/TFTC labels remain numeric until their separate mappings are verified. No game installation is required.
* Values: rating 0–24 (the per-rating mission table has 25 entries), rank 0–8, Kalidor Crescent 0–6, mission skips 0–3.

## XWVM — `.vmpilot`

* Source: XWVM `Assets/Code/GameState/*.cs` and `Assets/Code/GameMetadata`.
* XML: `<PilotRecord Name>` with one `<PilotGameRecord Id="xwing|tf|xvt|xwa">` per game, containing tour, historic,
  training, award, kill and statistic records.
* The editor lists everything XWVM reads, not only what the file holds: for X-Wing and TIE Fighter every tour
  (`tour1`-`tour5` with 12/12/14/20/20 operations; `battle1`-`battle13`), historical group (`xcombat`, `ycombat`,
  `acombat`, `bcombat`, `bonus`; `ship1`-`ship7`), proving-ground craft, the stored award (`kalidor` 0-5,
  `secretOrderRank` 0-8), kills and captures per craft, and the stat records. Medals, battle patches, historical coins,
  objective pins and flight badges are not stored; XWVM derives them from these records.
* A missing record is created only when one of its values is changed, with the attributes XWVM requires
  (a tour needs `CurrentTourStep`, or XWVM fails to load the pilot) and in XWVM's own element order.
* The document is edited in place, so unknown elements and attributes survive.
* Rules: the active tour must be started; a tour's step must be a real operation or 2147483647 (completed);
  every pilot should have all four game records (XWVM looks them up unchecked).

## Navigation tree

Each format arranges its fields for display (`PilotDocument.BeginLayout`); the file layout is not affected.

* Progression of the whole career sits at the top level; progression between tours (current tour, current battle)
  at the root of the tours; a tour's own progress (status, operations/missions completed) at the root of that tour.
* A mission with several saved values is its own sub-tree; a mission with a single value (X-Wing's tour scores)
  is listed at the tour's root. Missions, tournaments, battles and campaigns of XvT/BoP/XWA are sub-trees too.
* Slots that exist in the file but not in the game (X-Wing tours 6-8, TIE battles 14-20, unused mission slots and
  craft slots) are hidden while empty, and shown under "(not in the game)" / "Unused slot" when they hold data.
  Hidden fields are still validated and saved byte for byte.
* TIE Fighter's secret and bonus objective bytes are shown as one yes/no value per mission.
* X-Wing Alliance (and XWAUP, TFTC) group Campaign and Combat chamber missions by battle using mission lists built
  into the program (`src/StarfighterCompanion.Core/Data/MissionLists`, in the game's `MISSION.LST` format, read the
  way OpenXWA's `MissionSetup_LoadMissionList` reads it, localisation keys skipped). The Tour of Duty and Combat
  Chamber tables are both indexed by the mission's number in that list. The XWA list is included; Starfighter
  Companion never reads game files, and without a built-in list (XWAUP, TFTC for now) missions are listed by number.

## Converting

`src/StarfighterCompanion.Core/Conversion`.

* **X-Wing editions.** The 1993 and 1994 DOS editions write the same 1,706-byte record (XWVM `Xw93PilotFile`); the
  1998 edition the same fields in 1,705 bytes (`Xw98PilotFile`). The reserved block at 0x289 is 87 bytes in DOS and
  86 in 1998; the converter adds or drops that block's last byte. Every copy in the file is converted.
* **TIE Fighter editions.** Identical record. Battles that exist: 1–7 on the 1994 floppy (8–10 with Defender of the
  Empire), 1–13 on the CD-ROM and 1998 editions (OpenTIE loads `battleN.lfd` only when present; XWVM's `GameVariant`
  list names the add-ons). Converting to the floppy moves a current battle above 10 to the last battle it has.
* **Pilot names** are file names: 8 characters for DOS editions, 16 for 1998 (OpenXW register.h, OpenTIE register.c);
  letters, digits, `_` and `-`.
* **X-Wing → XWVM** follows `PilotGameRecord(IOriginalPilotFile, …)`: rank, status, score, `AiScore` = 4 × skill,
  Kalidor Crescent (`kalidor`, level − 1), proving grounds, historical tours `xcombat`/`ycombat`/`acombat`/`bcombat`/
  `bonus`, 24 kill/capture slots, and the stat records. Tours `tour1`…`tour5`: the progress byte counts completed
  operations (OpenXW `fediskio.c`), so it is the XWVM tour step; an operation's score is the better of its two
  alternative missions (operation tables from OpenXW `shipext.c`, identical to XWVM `todStages.json`).
* **TIE Fighter → XWVM** (XWVM has no importer): battles become tours `battle1`…`battle13` (step = mission cursor,
  completed = 2147483647), secret and bonus objective bits become `SecondaryGoals`/`BonusGoals`, the combat chamber
  becomes historic tours `ship1`…`ship7`, the Secret Order rank the `secretOrderRank` award (level = rank − 1), and
  kills map from TIE's 69 species to XWVM entity types (the Super Star Destroyer has none). A pilot lost in battle
  (status 2) is imported as captured.
* **XWVM → X-Wing/TIE** builds a new pilot the way each game's registration creates one (X-Wing: zeros with the
  Combat Chamber unlock bytes at 0xFF; TIE: version 1, training level 2) and fills it in. TIE files get two identical
  copies, X-Wing one. Every XWVM pilot written gets all four game records, because XWVM looks them up unchecked.

## Adding a format

1. Describe the record with `LayoutBuilder` (skip every byte you do not want to expose).
   `Build(size)` throws if the layout does not add up to the expected size.
2. Subclass `BinaryPilotDocument` and put cross-field checks in `CheckRules()`. Use `CommonRules.Broken` for
   problems that would break the game and `CommonRules.Odd` for warnings.
3. Register the format in `PilotFormats.All` and add tests.
