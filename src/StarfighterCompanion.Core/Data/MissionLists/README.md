# Built-in mission lists

Starfighter Companion never reads game files. To show X-Wing Alliance missions grouped by battle and with their
titles, the campaign mission lists are compiled into the program from this folder:

| File | Used for |
|---|---|
| `xwa.lst` | X-Wing Alliance and OpenXWA (included) |
| `xwaup.lst` | X-Wing Alliance Upgrade |
| `tftc.lst` | TIE Fighter Total Conversion |

The same list names both the Tour of Duty and the Combat Chamber records: the pilot file indexes both by the
mission's number in the campaign list.

The format is that of the game's `MISSIONS\MISSION.LST`: `//` comments, `[Section]` lines for battles, and three
lines per mission: its number in the pilot file, its file name (`* ` marks it locked until the previous one is done),
and its title. Localisation keys such as `!MISSION_9_DESC!` in front of a text are ignored. Without a list for an
edition, missions are shown by number.
