# VarVault — a tiered VaM var manager

> Working name; this repo formerly held an incomplete rewrite (now removed, preserved in git history).

A **new** desktop application for managing [Virt-a-Mate](https://hub.virtamate.com) `.var` packages at scale — **tiered, multi-drive object storage** with first-class dependency management.

## The problem it solves

A large VaM library (4 TB+, 70k+ `.var` files) across mixed drives (fast SSD + HDDs) is unmanageable: the game can't load with everything present, the fast drive fills up, and finding or fixing anything is painful. VarVault treats each var as an object, ranks each storage repository by real performance, classifies vars **hot / warm / cold** from actual usage, and places each on the tier that fits — keeping the SSD holding the right set automatically. It also does the things that make a big library usable: fast search, a visual gallery, dependency resolution, duplicate detection, and **auto-fixing the CJK-encoded vars that VaM can't load**.

Storage-class *principles* (à la S3 tiering), purpose-built for VaM.

## Status

**Design complete and sealed — build starting.**

- **[docs/new-app/](docs/new-app/README.md)** — the full design: core features, tech stack, data architecture (adversarially reviewed + perf-proven at 1M packages), UI prototype, and an item-level implementation checklist. Start at [docs/new-app/README.md](docs/new-app/README.md).
- **[docs/varManager/](docs/varManager/README.md)** — reverse-engineered spec of the legacy `varManager-MMDLoader` tool, kept as the feature reference / blueprint.
- Interactive UI draft: [`docs/new-app/mockups/prototype.html`](docs/new-app/mockups/prototype.html).

## Stack

.NET 10 · C# · Avalonia UI 12 (+ TreeDataGrid) · SQLite + FTS5 · EF Core 10.

## Approach

Built in thin vertical slices, each QC'd and benchmarked on real data; completeness is gated by the evidence-based [implementation checklist](docs/new-app/09-Implementation-Checklist.md).

**Slice order:** Repositories + Indexing + Library → Dependency engine → Activation + Presets → Duplicates + Encoding-fix → Analyzer + Migration.

## License

See [LICENSE](LICENSE).
