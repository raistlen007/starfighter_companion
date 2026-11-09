# Built-in mission lists

Starfighter Companion never reads game files. To show X-Wing Alliance campaign and Combat Chamber missions
grouped by battle and with their titles, the lists are compiled into the program from this folder:

| File | Used for |
|---|---|
| `xwa.campaign.lst` / `xwa.combat.lst` | X-Wing Alliance and OpenXWA |
| `xwaup.campaign.lst` / `xwaup.combat.lst` | X-Wing Alliance Upgrade |
| `tftc.campaign.lst` / `tftc.combat.lst` | TIE Fighter Total Conversion |

The format is that of the game's `MISSION.LST`: `//` comments, `[Section]` lines for battles, and three lines
per mission: its number in the pilot file, its file name, its title. Without a list, missions are shown by number.
