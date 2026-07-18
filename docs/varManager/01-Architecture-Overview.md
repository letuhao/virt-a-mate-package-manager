# 01 — Architecture Overview

## Solution layout

`varManager.sln` (VS 2022, format 12.00) contains five projects. Two are applications; the rest are libraries/controls.

| Project | Type | Framework | Role |
|---|---|---|---|
| **varManager** | WinExe (WinForms) | .NET Framework 4.8, x64 | The main VAR package manager. ~90% of all logic. |
| **MMDLoader** | WinExe (WPF + WinForms interop) | .NET 6.0-windows | Standalone GUI to convert MMD motions for VaM. Writes `loadscene.json`. |
| **LoadScene** | Library (Unity plugin / `MVRScript`) | .NET Framework 3.5 | In-game VaM plugin. Polls `loadscene.json`, applies scenes/presets/MMD motion. ILMerged. |
| **DgvFilterPopup** | Library | .NET Framework 4.8 | Excel-style column filter popups for `DataGridView`. |
| **DragNDrop** | Library | .NET Framework 4.8 | Drag-and-drop `ListView` helper (used by FormScenes). |

> Note: the older docs referenced `HUB`, `StarRatingControl`, and `ThreeStateTreeView` as separate projects. In this snapshot the star-rating and three-state-tree behaviors are implemented **inside** the varManager project (`ThreeStateTreeview.cs`, hand-drawn star `PictureBox`es in `HubItem`), and `HUB` is a stub. The five projects above are the ones in the solution.

## Two independent integration surfaces

varManager never controls VaM through an API. It integrates through **two file-based contracts** and one HTTP API:

```
                          ┌─────────────────────────────────────────┐
                          │  hub.virtamate.com / s3cdn.virtamate.com │  (HTTP JSON)
                          └───────────────▲─────────────────────────┘
                                          │ browse / find packages / updates
┌──────────────────────────┐             │
│  varManager (WinForms)    │─────────────┘
│  - repository (varspath)  │
│  - Access .mdb DB         │        symlinks          ┌───────────────────────────┐
│  - dependency graph       │────────────────────────► │  VaM install (vampath)     │
│  - scene/preview browser  │   AddonPackages\...\*.var │  AddonPackages\ (symlinks) │
└─────────────┬─────────────┘                          │  Saves\, Custom\           │
              │ writes                                  │  Custom\PluginData\feelfar\│
              │ loadscene.json                          │       loadscene.json  ◄────┼── polled by
              └────────────────────────────────────────►│                            │   in-game plugin
                                                        └───────────────────────────┘
   MMDLoader (WPF) ── copies VMD/audio + writes loadscene.json ──► same feelfar channel ──► LoadScene plugin
```

- **Symlink contract (⚠ load-bearing):** "install" = an NTFS symbolic link in `AddonPackages` pointing at the real `.var` in the repository. VaM sees a normal package. Uninstall = delete the link.
- **`loadscene.json` contract (⚠ load-bearing):** varManager (and MMDLoader) write `{vampath}\Custom\PluginData\feelfar\loadscene.json`; a cooperating in-game plugin ("feelfar", i.e. LoadScene) polls it, performs the load/preset/rescan, then deletes the file. See [06](./06-Preview-Scene-Analysis-and-Loading.md) and [09](./09-MMDLoader-and-LoadScene.md).
- **Hub API:** read-only browsing + link generation against `hub.virtamate.com`. varManager does not download files itself. See [08](./08-Hub-Integration-API.md).

## Layering (as-built vs. as-should-be)

**As-built:** there are effectively no layers. `Form1.cs` mixes UI event handlers, file I/O, ZIP extraction, Win32 P/Invoke, Access queries, LINQ-to-DataSet, and business rules in one class. The only separations are:
- `Comm.cs` — static filesystem/symlink/reparse-point helpers.
- `ZipHandler.cs` — static ZIP extraction/compression (SharpZipLib + 7-Zip shell-out).
- `SimpleLogger.cs` — file logger.
- `varManagerDataSet` — generated typed DataSet + TableAdapters (data access).
- The dialog forms (`FormHub`, `FormScenes`, `FormAnalysis`, …) — each still reaches back into `Form1` public methods for all business logic.

**As-should-be** (target for the rebuild — see [12](./12-Rebuild-Blueprint.md)): Core (domain) / Application (use-cases) / Infrastructure (DB, filesystem, zip, hub, VaM IPC) / Presentation (UI), with dependency injection and async throughout.

## Process & threading model (legacy)

- Single UI thread. Long operations run on a **`BackgroundWorker`** (`backgroundWorkerInstall`) dispatched by a string command argument (`"UpdDB"`, `"FillDataTables"`, `"MissingDepends"`, `"StaleVars"`, …).
- A single `Mutex` serializes the whole background pipeline; a second `Mutex` guards preview filtering.
- UI updates from the worker are marshaled via `BeginInvoke` delegates (`UpdateProgress`, `UpdateAddLoglist`, `UpdateVarsViewDataGridView`, …).
- The entire DataSet (all `vars`, `scenes`, `dependencies`) is **loaded into memory** at startup and queried with LINQ-to-DataSet; writes go back through TableAdapters per-row. This is the root of the startup-time and memory problems (see [Criticism](./Criticism-Document.md)).

## Key architectural decisions (and their consequences)

1. **Repository + symlinks instead of copying** — space-efficient, instant install/uninstall, and lets one physical `.var` be shared across AddonPackages "profiles". Requires Windows Developer Mode or admin to create symlinks. (⚠ load-bearing model.)
2. **Access `.mdb` via ACE OLEDB** — chosen for zero-setup single-file storage, but slow, 2 GB-limited, single-writer, and needs the Access Database Engine installed. App.config shows a **staged, unfinished migration to SQLite** (EF6 + System.Data.SQLite registered, no connection string yet). ✎ Rebuild target: SQLite + EF Core.
3. **Regex-based JSON scanning for dependencies** — deliberately tolerant of malformed/huge scene files; scans raw text for `"Creator.Package.Version":` keys anywhere, not strict JSON parsing. (See [04](./04-Dependency-Resolution-and-Versioning.md).) ✎ Preserve the *tolerance*, not the regex.
4. **7-Zip shell-out for the extract/re-zip round-trip** — 7z handles CJK entry names and is faster; SharpZipLib is used for encoding-aware single-archive work. Hardcoded path `C:\Program Files\7-Zip\7z.exe`. ✎ Rebuild: discover the binary or use `System.IO.Compression` for metadata reads.
5. **Special `___XXX___` directories** as semantic markers on disk (organized vars, quarantine, previews, links, profiles). (⚠ load-bearing — existing user libraries are laid out this way; see [02](./02-VAR-Format-and-Repository-Layout.md).)

## Technology stack summary

| Concern | Legacy | Rebuild target (recommended) |
|---|---|---|
| App framework | .NET Framework 4.8 (WinForms) | .NET 8/9 |
| UI | Windows Forms | Avalonia or WPF (MVVM) |
| DB | Access `.mdb` (ACE OLEDB) + typed DataSet | SQLite + EF Core |
| ZIP | SharpZipLib 1.4.2 + external 7z.exe | `System.IO.Compression` (+ optional 7z for rebuild) |
| Symlinks | Win32 P/Invoke (`kernel32`/`advapi32`) | `File/Directory.CreateSymbolicLink` + `ResolveLinkTarget` (.NET 6+) |
| JSON | SimpleJSON (vendored) + regex scanning | `System.Text.Json` (+ tolerant fallback) |
| Async | `BackgroundWorker` + `Mutex` + `Thread.Sleep` | `async`/`await` + `IProgress<T>` + `CancellationToken` |
| Logging | SimpleLogger (open/close per line) | Serilog/`ILogger` |
| MMD plugin | .NET 3.5 Unity plugin, ILMerged | unchanged (bound to VaM's Mono runtime) |

Continue to [02 — VAR Format & Repository Layout](./02-VAR-Format-and-Repository-Layout.md).
