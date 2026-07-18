# New App — Design Documentation

Design for a **new** VaM `.var` manager (working name **VarVault**) — a tiered, multi-repository object-storage system with first-class var/dependency management. **Not** a rebuild of the old varManager; it copies the old tool's strong *features* (var/dependency intelligence) but none of its architecture. The old tool's reverse-engineered spec lives in [`../varManager/`](../varManager/) as reference.

> Status: **design complete, sealed, build-ready.** No open questions ([10](./10-Decisions-Log.md)). Next: build Slice 1.

## The problem
4 TB+ of vars across heterogeneous drives (fast SSD + HDDs); VaM can't load with everything present; the SSD is full; managing identity/type/dependencies at 70k+ files is essential. Solution: rank storage by real performance, classify each var hot/warm/cold from usage, place it on the matching tier, and manage the whole library through its dependency structure — S3-style storage-class *principles*, purpose-built for VaM.

## Read in order

| # | Document | What it is |
|---|----------|-----------|
| 00 | [Core Feature](./00-Core-Feature.md) | The three pillars (repos/tiers · classifier/placement · var catalog) + activation |
| 01 | [Tech Stack](./01-Tech-Stack.md) | .NET 10 · Avalonia + TreeDataGrid · SQLite + FTS5 · EF Core |
| 02 | [Features](./02-Features.md) | ~90 requirement-level features, priority-tagged; out-of-scope sealed |
| 03 | [Data Architecture](./03-Data-Architecture.md) | The schema (v2, post-review): entities, mechanisms, performance design |
| 04 | [Data-Arch Review](./04-Data-Architecture-Review.md) | The 4-agent adversarial review + the 8 root-cause fixes folded into v2 |
| 05 | [Perf Spike Results](./05-Perf-Spike-Results.md) | Real SQLite spike at 1M packages — read path proven, one must-fix caught |
| 06 | [Feature Specs: Indexing](./06-Feature-Specs-Indexing.md) | Item-level spec + flagship algorithms (signature, encoding detect/fix) |
| 07 | [UI Coverage Map](./07-UI-Coverage-Map.md) | Every feature → a UI home; dead-end audit |
| 08 | [UI Design Review](./08-UI-Design-Review.md) | Critique of the mockup; high-priority fixes (all folded in) |
| 09 | [Implementation Checklist](./09-Implementation-Checklist.md) | Item-level, evidence-gated QC tracker (Phase 0 + Slices 1–5 + BE engines) |
| 10 | [Decisions Log](./10-Decisions-Log.md) | Every question, sealed |

## Interactive drafts
- **Full prototype** (all screens, clickable): [`mockups/prototype.html`](./mockups/prototype.html)
- **Detailed Library window**: [`mockups/main-window.html`](./mockups/main-window.html)

## Build approach
Thin vertical slices, each QC'd/benchmarked on **real data**; spec just-in-time. Completeness gate = the item-level [checklist](./09-Implementation-Checklist.md) — items check off only with concrete evidence.

**Slice order:** 1 Repos + Indexing + Library → 2 Dependency engine → 3 Activation + Presets → 4 Dedup + Encoding-fix → 5 Analyzer + Migration.
