# 12 — Rebuild Blueprint

This chapter distills the whole reverse-engineering effort into a decision guide for the rebuild: the **contracts you must preserve**, the **implementation choices you should replace**, and a **recommended architecture**. Read this first, then drill into the referenced chapters.

## Part A — Load-bearing contracts (⚠ preserve exactly)

These are compatibility boundaries with VaM, the hub, or existing user data on disk. Changing any of them silently breaks real installations.

| # | Contract | Where |
|---|----------|-------|
| A1 | **VAR naming rule** `Creator.Package.Version` (3 dot-parts, version = digits; `latest` valid only as a dependency ref). | [02](./02-VAR-Format-and-Repository-Layout.md) |
| A2 | **`.var` = ZIP with a top-level `meta.json`**; VaM-compatible rebuild = ZIP + Deflate + level 5 (never LZMA). | [02](./02-VAR-Format-and-Repository-Layout.md), [05](./05-Installation-Symlinks-and-Profiles.md) |
| A3 | **The `___XXX___` special directories** (repository layout, quarantine, previews, link dirs, and `___AddonPacksSwitch ___` *with its trailing space*). | [02](./02-VAR-Format-and-Repository-Layout.md) |
| A4 | **Install = NTFS symlink** in `AddonPackages\___VarsLink___\` → real repo file; uninstall = delete link; `.disabled` marker = installed-but-disabled. | [05](./05-Installation-Symlinks-and-Profiles.md) |
| A5 | **`loadscene.json` IPC** at `{vampath}\Custom\PluginData\feelfar\` — the exact resource `type` vocabulary, the `Creator.Pkg.Ver:/entry` `saveName` form (vs. `MMDForLoad/…` relative vs. absolute for MMD), and the misspelled types `empytscene`/`atomSubscene`. Consumed and deleted by the in-game plugin. | [06](./06-Preview-Scene-Analysis-and-Loading.md), [09](./09-MMDLoader-and-LoadScene.md) |
| A6 | **Dependency semantics** — recursive `meta.json` dependency map keyed by `Creator.Package.(N\|latest)`; version matching = "requested-or-next-newer, else newest-older"; forward closure for install, guarded reverse closure for uninstall. | [04](./04-Dependency-Resolution-and-Versioning.md) |
| A7 | **Hub API** — `hub.virtamate.com/citizenx/api.php` actions (`getInfo`/`getResources`/`getResourceDetail`/`findPackages`) + `s3cdn.virtamate.com/data/packages.json`. | [08](./08-Hub-Integration-API.md) |
| A8 | **On-disk hide/fav markers** (`AddonPackagesFilePrefs\**\*.hide`/`*.fav`) and **missing-var alias links** (`___MissingVarLink___`). Migrate, don't ignore. | [07](./07-Database-Schema.md), [10](./10-UI-Feature-Catalog.md) |
| A9 | **FormAnalysis storable-id allow-lists** (skin/pose/breast/glute id sets, clothing/hair internalId material matching, `SELF:/`→`var:/` rewrite). Copy verbatim. | [06](./06-Preview-Scene-Analysis-and-Loading.md) |
| A10 | **MMD retargeting tables** — `DazBoneMapping`, `FaceMorph`, finger maps, `g2f.pmx`, `(0,1,0,0)` axis fix. LoadScene stays on .NET 3.5. | [09](./09-MMDLoader-and-LoadScene.md) |

## Part B — Implementation choices to replace (✎)

| Legacy | Problem | Replace with |
|---|---|---|
| Access `.mdb` + ACE OLEDB + typed DataSet | Slow, 2 GB cap, single-writer, needs engine installed, no FKs/indexes | **SQLite + EF Core**, real FKs + indexes, the schema in [07](./07-Database-Schema.md) |
| Full DataSet loaded into memory, LINQ-to-DataSet | 500 MB–2 GB RAM, minutes to start | Query the DB directly; virtualized/paged reads; project only what the grid needs |
| `Form1.cs` God object (3,743 lines) | Untestable, unmaintainable | Clean layers + DI (Part C) |
| `BackgroundWorker` + `Mutex` + `Thread.Sleep(20000)` + `Application.DoEvents` | UI freezes, no cancel, re-entrancy | `async`/`await` + `IProgress<T>` + `CancellationToken` |
| WinForms | No virtualization, dated | Avalonia (cross-platform) or WPF, MVVM |
| Win32 P/Invoke symlink/reparse layer | Windows-only, 32-bit-hazard handle check | `File/Directory.CreateSymbolicLink` + `ResolveLinkTarget` (.NET 6+); hard link via P/Invoke only if needed |
| 7-Zip shell-out, hardcoded path, whole-archive extract | Fragile, needs 7-Zip, no partial reads | `System.IO.Compression.ZipArchive` for metadata/preview reads; keep an optional 7z path for full re-zip only |
| Regex-per-entry content classification | Recompiles thousands of regexes | Precompiled regex set / prefix switch; index files in parallel |
| Regex JSON dependency scan with `$`/`^` string sentinels | Stringly-typed, error-prone | Tolerant JSON reader; model `$`/`^` as fields (`VersionSubstituted`, work-set state) |
| UpdDB idempotency by name only | Edited vars never re-index | Freshness by content hash / mtime |
| `installStatus` delete-all-then-reinsert | Race-prone snapshot | 1:1 child of `vars`, upsert |
| Encoding round-trip via `Encoding.Default` | Breaks on .NET Core (Default = UTF-8) | Register `CodePagesEncodingProvider`; read raw entry-name bytes |
| SimpleLogger (open/close per line) | Slow, no rotation | Serilog / `ILogger` |

## Part C — Recommended architecture

```
VirtaMatePackageManager.Core            // domain: PackageId, VarPackage, Dependency graph, VersionRef
VirtaMatePackageManager.Application     // use-cases: IndexRepository, ResolveDependencies,
                                        //   Install/Uninstall, LoadScene, ScanHub, DetectStale  (all async)
VirtaMatePackageManager.Infrastructure  // SQLite/EF Core repos, filesystem+symlink service,
                                        //   ZipService, HubClient, VamIpcWriter (loadscene.json)
VirtaMatePackageManager.Presentation    // Avalonia/WPF MVVM: library grid, preview browser,
                                        //   scene manager, hub browser, analysis, settings
tests/                                  // Core + Application are now unit-testable
```

Key domain types to introduce (replacing the string-sentinel soup):
- `PackageId { string Creator; string Package; int Version; }` with canonical `ToString()`.
- `VersionRef` = `Exact(int)` | `Latest`.
- `ResolvedDependency { PackageId? Resolved; bool VersionSubstituted; bool Missing; }` (replaces `$`).
- `ISymlinkService`, `IZipService`, `IVarRepository`, `IHubClient`, `IVamSession` (writes `loadscene.json`, checks the `vam` process).

Cross-cutting wins:
- **Async + cancellation** everywhere; progress via `IProgress<(int done, int total, string msg)>`.
- **Parallel indexing** across files (bounded by CPU); batched DB writes in transactions.
- **Virtualized** library and preview lists; bounded image cache (the legacy's 20-image LRU is a fine baseline).
- **Multi-repository** support (the attempted rewrite wanted this) — the legacy has a single `varspath`; generalize `varPath` to `(repositoryId, relativePath)`.
- **Transactional operations** — the legacy is "log-and-continue", so partial failures leave DB/filesystem inconsistent (reconciled only by re-running UpdDB). Make install/uninstall/index atomic where possible, with a reconcile pass as backstop.

## Part D — Suggested build order

1. **Domain + schema** — `PackageId`, dependency graph, the SQLite schema ([07](./07-Database-Schema.md)); port the migration from Access.
2. **Repository indexing** ([02](./02-VAR-Format-and-Repository-Layout.md)–[03](./03-Indexing-and-Content-Classification.md)) — Tidy, UpdDB (ZipArchive-based, parallel), preview extraction, content classification.
3. **Dependency engine** ([04](./04-Dependency-Resolution-and-Versioning.md)) — version matching + forward/reverse closures, with unit tests against the worked example.
4. **Install/symlink service + profiles** ([05](./05-Installation-Symlinks-and-Profiles.md)) — the core install/uninstall/switch flows.
5. **Preview/scene browser + loading** ([06](./06-Preview-Scene-Analysis-and-Loading.md)) — the `loadscene.json` writer + temp-link lifecycle.
6. **Hub integration** ([08](./08-Hub-Integration-API.md)) — browse + missing/update scans; optionally add authenticated download.
7. **FormAnalysis preset extraction** ([06 §FormAnalysis](./06-Preview-Scene-Analysis-and-Loading.md)) — the hardest; port the id allow-lists verbatim.
8. **MMD** ([09](./09-MMDLoader-and-LoadScene.md)) — modernize only the MMDLoader GUI + JSON writer; leave LoadScene on .NET 3.5.

Start each step from the referenced chapter; anything marked ⚠ there is a contract, anything marked ✎ is a place to improve.

---

*This blueprint, and chapters [01](./01-Architecture-Overview.md)–[11](./11-Build-Dependencies-and-External-Tools.md), were reverse-engineered directly from `F:\varManager-MMDLoader_v1.0.1.0`. The legacy build stays in git history; this spec is what carries its knowledge into the rebuild.*
