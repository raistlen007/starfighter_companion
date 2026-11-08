# Starfighter Companion

A small, standalone pilot file editor for the Star Wars X-Wing series.
Open a pilot, change anything the game stores about it, and save — without being able to put the pilot
into a state that the game cannot handle.

| Game | File | Notes |
|---|---|---|
| X-Wing (1993 floppy, 1994 CD-ROM, 1998 Windows, GOG/Steam) | `.plt` | DOS and Windows layouts are told apart by size |
| TIE Fighter (1994 floppy, 1995 CD-ROM, 1998 Windows, GOG/Steam) | `.tfr` | current + backup record are kept in step |
| X-Wing vs. TIE Fighter | `.plt` | |
| Balance of Power | `.pl2` | BoP also keeps an XvT `.plt` next to it |
| X-Wing Alliance, XWA Upgrade (XWAUP), TIE Fighter Total Conversion (TFTC) | `.plt` | |
| OpenXW, OpenTIE, OpenXvT, OpenXWA | as the original games | they read and write the original formats |
| XWVM | `.vmpilot` | XML; unknown content is preserved |

## Download and run

Grab the file for your system from the [Releases](../../releases) page. There is nothing to install:
the .NET runtime is built into the program.

* **Windows:** `StarfighterCompanion-win-x64.exe`
* **Linux / Steam Deck (desktop mode):** `StarfighterCompanion-linux-x64` — make it executable (`chmod +x`) and run it.
* **macOS:** `StarfighterCompanion-osx-arm64.zip` (Apple Silicon) or `-osx-x64.zip` (Intel). The app is not signed, so the first time
  right-click it and choose **Open**.

Close the game before editing its pilots — most of them write the pilot file when they exit.

## How it keeps your pilot safe

* **Only known layouts are opened.** A file is recognised by its extension and exact size (or, for XWVM, its XML root).
  Anything else is refused instead of being guessed at.
* **Every value has limits.** Numbers are limited to what the game allows (rank 0–5, difficulty Easy/Medium/Hard, …),
  choices are drop-downs, flags are check boxes, and names are limited to plain ASCII that fits the game's buffer.
  An illegal value is refused as you type and never reaches the file.
* **Values that depend on each other are checked.** For example, only one X-Wing tour may be active and it must be
  the current tour; an XWVM pilot's active tour must exist. Problems that would break the game are **errors**
  and block saving; things that are merely unusual are **warnings**.
  Problems that were already in the file are reported as warnings, so an untouched pilot can always be saved again.
* **Unknown bytes are never touched.** Saving a file without edits writes it back byte for byte.
* **Backups.** The first time a file is overwritten, the original is kept as `name.ext.original.bak` (never replaced),
  and every save keeps the previous version as `name.ext.bak`. Files are written to a temporary file first and then
  swapped in, so a crash cannot leave half a pilot behind.

## Building

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet test                                   # run the tests
dotnet run --project src/StarfighterCompanion # run the editor

# single-file, self-contained builds
dotnet publish src/StarfighterCompanion -c Release -r win-x64   -o publish/win-x64
dotnet publish src/StarfighterCompanion -c Release -r linux-x64 -o publish/linux-x64
dotnet publish src/StarfighterCompanion -c Release -r osx-arm64 -o publish/osx-arm64
```

Pushing a tag such as `v0.1.0` makes GitHub Actions build all platforms and attach them to a release.

### Project layout

* `src/StarfighterCompanion.Core` — file formats, validation rules. No UI code; this is where the knowledge lives.
  * `Binary/` — a small layout engine: each binary format is described field by field, and the build fails if
    a single byte is unaccounted for.
  * `Formats/<Game>/` — one file per game with its layout, value limits and rules.
* `src/StarfighterCompanion` — the desktop app (Avalonia).
* `tests/` — unit tests. Put real pilot files in `tests/samples/` (any sub-folder) and the tests will check that
  each one opens and is saved back unchanged.

See [docs/FORMATS.md](docs/FORMATS.md) for the details of each format.

## Credits

File layouts are based on the reverse-engineering work of the **OpenXW, OpenTIE, OpenXvT and OpenXWA** projects
(GPL-3.0) and on **XWVM**'s pilot code.

## License

[GNU Affero General Public License v3.0](LICENSE).

Star Wars, X-Wing, TIE Fighter and related names are trademarks of Lucasfilm Ltd.
This project is not affiliated with or endorsed by Lucasfilm, Disney, LucasArts or Totally Games.
You need your own copy of the games.
