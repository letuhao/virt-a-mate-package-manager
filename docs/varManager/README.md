# varManager-MMDLoader — Reverse-Engineered Specification

This directory is a **complete, source-accurate technical specification of the legacy `varManager-MMDLoader` v1.0.1.0** system (located at `F:\varManager-MMDLoader_v1.0.1.0`). It was produced by reading the actual source (not inferred from behavior), and it exists to serve as the **blueprint for a clean rebuild**.

> The legacy app is a .NET Framework 4.8 WinForms tool whose main window (`Form1.cs`) is a single 3,743-line "God object", backed by a Microsoft Access `.mdb` database, that manages Virt-a-Mate (VaM) `.var` packages by keeping a separate library and creating **NTFS symbolic links** into VaM's `AddonPackages` directory. It works but is unmaintainable and slow; the plan is to keep it in git history and rebuild on a modern stack. These docs capture *what it does and why* so nothing of value is lost in the rewrite.

## What varManager is for

VaM content ships as **`.var` files** (ZIP archives named `Creator.Package.Version.var`) that pile up by the thousands. VaM loads every `.var` sitting in `AddonPackages`, which is slow and unwieldy. varManager solves this by:

1. Keeping all `.var` files in a **central repository** (`varspath`), organized and de-duplicated.
2. **"Installing"** a package = creating a *symlink* to it inside `AddonPackages` (no copy, instant, reversible).
3. Indexing every package's **metadata, content types, preview images, and dependencies** into a database.
4. Resolving the **dependency graph** so installing a scene also installs everything it needs.
5. Browsing/loading **scenes and presets** into a running VaM session, and integrating with the **hub.virtamate.com** content site to find missing packages and updates.
6. A secondary **MMD → VaM motion loader** (MMDLoader + the in-game LoadScene plugin) that converts MikuMikuDance motions into VaM Timeline animations.

## Document map

| # | Document | Covers |
|---|----------|--------|
| 01 | [Architecture Overview](./01-Architecture-Overview.md) | Solution layout, projects, layers, process model, key design decisions |
| 02 | [VAR Format & Repository Layout](./02-VAR-Format-and-Repository-Layout.md) | `.var`/`meta.json` format, naming rule, the `___XXX___` special directories, `TidyVars` organization |
| 03 | [Indexing & Content Classification (UpdDB)](./03-Indexing-and-Content-Classification.md) | How a `.var` is scanned into the DB: content-type regex table, preview extraction, counts |
| 04 | [Dependency Resolution & Versioning](./04-Dependency-Resolution-and-Versioning.md) | Forward/reverse dependency closures, version matching, the `$`/`^` sentinels, missing-dependency detection |
| 05 | [Installation, Symlinks & Profiles](./05-Installation-Symlinks-and-Profiles.md) | `VarInstall`, the Win32 symlink/reparse layer, temp links, AddonPackages profile switching, ZIP handling |
| 06 | [Preview, Scene Analysis & Loading](./06-Preview-Scene-Analysis-and-Loading.md) | Preview pipeline, scene decomposition, FormAnalysis preset extraction, PrepareSaves, the `loadscene.json` VaM hand-off |
| 07 | [Database Schema](./07-Database-Schema.md) | The 9 legacy tables/views (exact columns), the missing relational integrity, and a proposed modern schema |
| 08 | [Hub Integration API](./08-Hub-Integration-API.md) | The hub.virtamate.com `citizenx/api.php` endpoints, request/response shapes, update/missing scans |
| 09 | [MMDLoader & LoadScene](./09-MMDLoader-and-LoadScene.md) | The MMD motion subsystem and the `loadscene.json` IPC contract in full |
| 10 | [UI Feature Catalog](./10-UI-Feature-Catalog.md) | Every form and its workflow (Form1 + 13 dialogs) |
| 11 | [Build, Dependencies & External Tools](./11-Build-Dependencies-and-External-Tools.md) | Target frameworks, NuGet/external deps (7-Zip, ACE OLEDB), ILMerge, runtime asset requirements |
| 12 | [Rebuild Blueprint](./12-Rebuild-Blueprint.md) | Synthesis: load-bearing behaviors to preserve, traps to avoid, and a recommended modern architecture |
| — | [Criticism Document](./Criticism-Document.md) | Catalog of the legacy system's architectural/performance/quality problems |

## How to use this for the rebuild

- **[12-Rebuild-Blueprint](./12-Rebuild-Blueprint.md)** is the starting point — it distills the "must-preserve" contracts (naming rule, special dirs, `loadscene.json` schema, hub API, dependency semantics) from the "should-replace" implementation choices (Access DB, God object, synchronous I/O, Win32 P/Invoke).
- Everything marked **⚠ load-bearing** is a compatibility contract with VaM, the hub, or existing user data on disk — changing it silently breaks things.
- Everything marked **✎ rebuild note** is a legacy weakness with a recommended modern replacement.

---

*Source of record: `F:\varManager-MMDLoader_v1.0.1.0`. All method names, line references, regexes, table/column names, and API endpoints below were extracted directly from that source.*
