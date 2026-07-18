# New App — Tech Stack (agreed)

Decisions locked in during design discussion. Status: **agreed** unless marked TBD.

## Constraints that drove the choices
- **Desktop app, single-user, no server/client** — everything runs locally.
- **Windows-bound in practice** — VaM, symlinks (`AddonPackages`), and the whole workflow are Windows. Cross-platform is a free bonus, not a requirement.
- **~70K+ `.var` files today, growing** — the UI and DB must stay fast at this scale and 10× beyond. The old varManager was slow due to *architecture* (whole-DataSet-in-memory, LINQ-to-DataSet, no indexes, synchronous UI-thread I/O, Access), not data size. 70K rows is trivial for any properly-indexed store.
- Heavy **search / sort / filter / findability** requirements (the "I lost my favorite var in the pile" problem).

## The stack

| Layer | Choice | Notes |
|---|---|---|
| **Runtime** | **.NET 10** (current LTS) | Newest; C#. |
| **Language** | **C#** | Domain is C#-native (VaM, symlink P/Invoke, ZIP). |
| **UI framework** | **Avalonia UI 12** (XAML + MVVM) | The modern "successor to WPF". SkiaSharp rendering; desktop-first controls; runs on .NET 10. |
| **Main data view** | **Avalonia TreeDataGrid** | Purpose-built, fully virtualized; handles 100K+ rows with sort/filter/hierarchy. Direct fix for the old tool's scroll/sort pain. |
| **MVVM helpers** | **CommunityToolkit.Mvvm** | `ObservableObject`, `[RelayCommand]`, source-generated — TBD but the default. |
| **Database** | **SQLite** | Embedded, single-file, zero-install (no ACE OLEDB dependency). Fast at our scale. |
| **Search** | **SQLite FTS5** | Full-text index over creator/package/tags/description/content for instant ranked search + discovery. |
| **ORM / data access** | **EF Core 10** (primary) | Entities, LINQ, change tracking, **migrations** (schema evolution). |
| **Raw-SQL escape hatch** | `FromSqlRaw` / `Microsoft.Data.Sqlite` | For **FTS5 `MATCH`** queries and **bulk indexing** (batched inserts of tens of thousands of vars) — the two hot paths where LINQ/change-tracking is the wrong tool. |
| **Analytics engine** | **DuckDB** — *optional, later* | Columnar/OLAP. Only if the usage-statistics/hot-cold analyzer (Pillar 2) needs heavy time-series aggregation. Start without it. |
| **DI / hosting** | `Microsoft.Extensions.DependencyInjection` (+ `Hosting`) | Constructor injection; testable services. |
| **Logging** | `Microsoft.Extensions.Logging` + **Serilog** | Structured, async, file sink. Replaces the old open/close-per-line `SimpleLogger`. |
| **Async** | `async`/`await` + `IProgress<T>` + `CancellationToken` everywhere | No `BackgroundWorker`, no `Thread.Sleep`, no `Application.DoEvents`. |
| **Symlinks** | `File.CreateSymbolicLink` / `Directory.CreateSymbolicLink` + `ResolveLinkTarget` (.NET 6+) | Replaces the old Win32 P/Invoke layer. Hard links via P/Invoke only if ever needed. |
| **ZIP / var reads** | `System.IO.Compression.ZipArchive` | Read only `meta.json` / previews without full extraction. Optional external 7-Zip kept **only** for full VaM-compatible re-zip (ZIP + Deflate + level 5). |
| **JSON** | `System.Text.Json` | With a tolerant path for scanning messy scene/meta files (the old tool used regex-over-raw-text for resilience; keep the resilience). |
| **Testing** | xUnit (+ FluentAssertions) — TBD | Core + Application layers are unit-testable by design. |

## Data-access policy (EF Core + raw SQL)
- **Default:** EF Core LINQ for all normal reads/writes and the domain model.
- **FTS5 search:** raw SQL `MATCH` via `FromSqlRaw`, mapped back to entities/DTOs. Keep the FTS index in sync with the `vars` table via triggers or on write.
- **Bulk indexing** (scan repo → insert/update thousands of vars): batched transactions via `Microsoft.Data.Sqlite` / `ExecuteSqlRaw`, not per-entity `Add` + `SaveChanges`, to avoid change-tracker overhead.
- **Dependency-graph closures:** recursive CTE in SQL, or compute in memory once (both fast at 70K); benchmark later.
- **Migrations:** EF Core migrations own schema evolution; ship a first migration + a one-time importer if we ever pull data from an existing library.

## Solution shape (layers — detail TBD in a later doc)
```
<App>.Core            // domain: PackageId, VarObject, Repository, Tier, Dependency graph, VersionRef
<App>.Application     // use-cases (async): IndexRepository, ClassifyUsage, PlaceObject,
                      //   ResolveDependencies, Activate/Deactivate, Search
<App>.Infrastructure  // EF Core + SQLite, FTS5, filesystem+symlink service, ZipService,
                      //   HubClient, VamIpc (loadscene.json), performance profiler
<App>.Desktop         // Avalonia app: TreeDataGrid views, view-models, DI wiring
tests/                // Core + Application unit tests
```

## Explicitly NOT using (and why)
- **Microsoft Access / OLEDB** — the old tool's DB; slow, single-writer, needs the engine installed.
- **In-memory DataSet / TableAdapters** — root cause of the old tool's memory + startup + sort problems.
- **WPF** — outdated; Avalonia is the modern equivalent.
- **WinUI 3** — considered; Windows-only is fine, but weaker data-grid story and rockier tooling than Avalonia + TreeDataGrid.
- **MAUI** — mobile-first, poor for heavy desktop data grids, no real Linux.
- **PostgreSQL / SQL Server** — require a running service; violates "no server".

---

*Open items to revisit: MVVM toolkit confirmation, EF Core vs. hybrid split boundaries once schema is drafted, whether/when DuckDB enters, test framework.*
