# VarVault — a tiered VaM var manager

A desktop application for managing Virt-a-Mate `.var` packages at scale — **tiered, multi-drive object storage** with first-class dependency management, duplicate reclaim, a visual gallery, and CJK-encoding auto-fix.

## The problem it solves

A large VaM library (4 TB+, tens of thousands of `.var` files) across mixed drives (fast SSD + HDDs) is unmanageable: the game can't load with everything present, the fast drive fills up, and finding or fixing anything is painful. VarVault treats each var as an object, ranks each storage repository by real performance, classifies vars **hot / warm / cold** from actual usage, and places each on the tier that fits — keeping the SSD holding the right set automatically. It also does the things that make a big library usable: fast search, a visual gallery, dependency resolution, duplicate detection, and **auto-fixing the CJK-encoded vars that VaM can't load**.

Storage-class *principles* (à la S3 tiering), purpose-built for VaM.

## What it does

A full 13-screen desktop app wired to a real catalog engine (SQLite):

- **Index & browse** — walks your repositories, extracts var metadata + **preview thumbnails**, and shows the library in a searchable (FTS5) table or a **visual gallery**. Per-creator facets, tier/class columns, saved views, tags.
- **Tiering & migration** — classifies hot/warm/cold from usage, proposes placement, and moves files across drives with a durable copy → verify → rename → delete flow (nothing is lost mid-move).
- **Dependencies** — resolves the full closure, flags missing refs, and resolves them via aliases to owned packages.
- **Duplicates & reclaim** — content-signature dedup (catches same-content/different-zip cases whole-file hashes miss) → reclaim redundant copies safely.
- **Encoding health** — detects mojibake (GBK / Shift-JIS / …) and rewrites a clean UTF-8 var, keeping the original.
- **Presets, rescue, trash & backup** — activate curated sets, get the game launching again, and never hard-delete (predicate-gated → recoverable trash + auto-backed-up catalog).

## Status

**Built and working**, exercised end-to-end over a real `.var` corpus.

- **0 build errors · 563 tests green** (unit + integration + real-repo E2E). The full-app walkthrough registers real repositories, runs real indexing/extraction, browses the populated library, renders real gallery previews, and performs real cross-drive moves — all driving the actual application window.
- Design docs: **[docs/new-app/](docs/new-app/README.md)** (features, data architecture — adversarially reviewed + perf-proven at 1M packages — UI prototype, and item-level checklists).

## Stack

.NET 10 · C# · Avalonia UI 11.2 · SQLite + FTS5 · EF Core 10 (WAL, single-writer). Tested with xUnit + Avalonia.Headless.

## Architecture

A modular monolith: independent feature **modules** that talk only through the **SDK** (contracts + events), over EF Core + SQLite. A module never references another module — cross-module work goes through SDK interfaces + an event bus, enforced by architecture tests.

```
Common ← Sdk ← Domain ← Modules.* / Infrastructure ← Host ← Cli / App
```

See [docs/new-app/11-Architecture-and-Modularity.md](docs/new-app/11-Architecture-and-Modularity.md).

## Build, test, run

Solution is **`VarVault.slnx`** (new XML format):

```bash
dotnet build                        # 0 errors expected
dotnet test                         # all green expected
dotnet run --project src/VarVault.App   # launch the desktop app
```

.NET 10 is pinned in `global.json`; packages are centrally managed in `Directory.Packages.props`.

## License

See [LICENSE](LICENSE).
