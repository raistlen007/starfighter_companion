# Built-in mission lists

Starfighter Companion never reads game files. To show X-Wing Alliance missions grouped by battle and with their
titles, the mission lists of each version are compiled into the program from this folder:

| File | Used for |
|---|---|
| `xwa.lst` | X-Wing Alliance, OpenXWA, XWA Upgrade 2025 and 2020 (the Upgrade keeps the original campaign) |
| `embers.lst` | Embers of the Empire |
| `tftc.lst` | TIE Fighter Total Conversion Classic |
| `tftcr.lst` | TIE Fighter Total Conversion Reimagined |
| `tftc.combat.lst` | TFTC Combat Chamber (its `COMBAT\MISSION.LST`; the same in Classic and Reimagined) |

Without a `.combat.lst`, the campaign list also names the Combat Chamber records: the pilot file indexes both by
the mission's number in the campaign list.

The format is that of the game's `MISSIONS\MISSION.LST`: `//` comments, `[Section]` lines for battles, and three
lines per mission: its number in the pilot file, its file name (`* ` marks it locked until the previous one is done),
and its title. Localisation keys such as `!MISSION_9_DESC!` in front of a text are ignored. Without a list for an
edition, missions are shown by number.
