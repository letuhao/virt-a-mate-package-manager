# 10 — Decisions Log (sealed)

Every open question across the design, resolved. **Sealed** = the decision the build proceeds on. Sealed ≠ irreversible — each notes how to revisit — but nothing here is "TBD" anymore.

## Scope

| # | Question | Decision | Rationale / reversal |
|---|----------|----------|----------------------|
| S1 | Hub browsing (hub.virtamate.com) | **OUT of v1** | The core mission is local tiered storage + var management, not downloading. It's a self-contained subsystem that can be added later as an **optional plugin/module** against the same 5 endpoints (documented in [docs/varManager/08](../varManager/08-Hub-Integration-API.md)) without touching the core. |
| S2 | Load content into a running VaM (`loadscene.json` + in-game plugin) | **OUT of v1** | Couples us to VaM's plugin runtime; orthogonal to storage management. Reversible later; the contract is documented in [docs/varManager/09](../varManager/09-MMDLoader-and-LoadScene.md). "Activation" (which vars the game sees) IS in scope; *launching a scene* is not. |
| S3 | Scene/preset extraction, MMD loader, var packaging | **OUT** | Content creation, not management. Separate concerns. |
| S4 | App name | **"VarVault"** (working name, provisional) | Placeholder used throughout the mockups. The single decision most trivial to change later (one rename). Owner may override anytime before public release. |

## Data / storage

| # | Question | Decision | Rationale |
|---|----------|----------|-----------|
| D1 | FTS5 tokenizer for space-less CJK | **`trigram`** tokenizer | Segments CJK without spaces and gives substring matching; no ICU dependency. Revisit if ranking quality on Latin text suffers → add a second FTS table. |
| D2 | Thumbnail store format | **Dedicated SQLite `thumbs.db`** keyed by PackageId (blobs) | One file, easy to back up/relocate, no 700k loose files; on the fastest tier. Revisit only if blob size hurts (→ sprite atlases). |
| D3 | `OrderedSnapshot` lifetime | **Per-session, in-memory temp table**, rebuilt on filter/sort change | Spike proved build ≈200 ms/500k — cheap to rebuild. Persist only the default view later if desired. |
| D4 | Hysteresis / window defaults | **30d & 90d** windows; `Class` flips only when score delta ≥ **15%** AND ≥ **7 days** since last flip | Concrete starting values; tunable in Settings. `LastFlipAt` persisted for reproducibility. |
| D5 | FAT/exFAT/network mtime granularity | **2-second tolerance window** on removable/network repos for freshness compare | Avoids spurious re-index; NTFS uses exact compare. |
| D6 | Tier auto-assign thresholds | **>3000 MB/s → T1, >800 → T2, else T3** (configurable) | Sensible defaults for NVMe/SATA-SSD/HDD; user-overridable per repo. |
| D7 | ORM split | **EF Core 10 primary**; raw SQL via `Microsoft.Data.Sqlite` for **FTS5 MATCH** + **bulk indexing** | Standard EF pattern; hot paths bypass change-tracker. |

## Architecture / stack (confirming earlier calls)

| # | Question | Decision |
|---|----------|----------|
| A1 | Runtime / language | **.NET 10 / C#** |
| A2 | UI framework | **Avalonia 12 + TreeDataGrid**, MVVM |
| A3 | MVVM toolkit | **CommunityToolkit.Mvvm** |
| A4 | Database | **SQLite + FTS5**, single file, pinned to stable local storage |
| A5 | Logging | **Serilog** (+ `Microsoft.Extensions.Logging`) |
| A6 | DI | **Microsoft.Extensions.DependencyInjection** (+ Hosting) |
| A7 | Test framework | **xUnit + FluentAssertions** |
| A8 | Preset switching | **Directory-swap profiles** (instant) |
| A9 | Gallery scrolling | **Materialized read table + OrderedSnapshot** (O(1) random access) |
| A10 | Automation | **Propose, never auto-destroy**; single-copy hard-gated; auto-migrate = explicit opt-in |
| A11 | Identity case-folding | **App-computed Unicode fold key** (NFC + full case-fold) — heavy-CJK library |

## Model / behavior (from the adversarial review, now firm)

| # | Decision |
|---|----------|
| M1 | Identity = **verbatim filename**; facets parsed from filename, never `meta.json` |
| M2 | Physical facts (`ContentItem`, extracted deps, counts) keyed on **VarFile**; Package values from an elected canonical |
| M3 | `CanonicalVarFileId` nullable, `ON DELETE SET NULL`, deterministic re-election; VarFile delete never cascades to Package |
| M4 | **One deletion predicate** gates every delete path (same identity + online + full-hash-verified + not last online copy) |
| M5 | Durability: temp-write → flush → cache-bypassed verify → atomic rename → delete source; `synchronous=FULL` on destructive txns |
| M6 | DB + trash are core safety infra (auto backup, integrity check, per-item trash manifest) |
| M7 | Single-writer recompute pipeline owns all derived state with defined transaction boundaries |
| M8 | Reverse closure precomputed (`ReverseDependentCount` + `IsFoundational`), never enumerated on the interactive path |
| M9 | User saves modeled (`UserSave`/`SaveDependency`) and included in orphan/safe-delete reverse closure |
| M10 | Aliases persistent (global + per-preset), serialized by name (portable), real match outranks alias |

## Build approach

| # | Decision |
|---|----------|
| B1 | Build in **thin vertical slices**, each QC'd/benchmarked on real data; spec just-in-time (not big-upfront specs) |
| B2 | Slice order: 1 Repos+Indexing+Library → 2 Dependency engine → 3 Activation+Presets → 4 Dedup+Encoding-fix → 5 Analyzer+Migration |
| B3 | Completeness gate = the item-level [09-Implementation-Checklist](./09-Implementation-Checklist.md); `[x]` only with concrete evidence |

## Feature specs (sealed separately)

| # | Decision |
|---|----------|
| F1 | **Import &amp; Dedup-Review** feature sealed 2026-07-20 in [30-Import-And-Dedup-Review-Spec §13](./30-Import-And-Dedup-Review-Spec.md#13-decisions-log--sealed) (checklist [31](./31-Import-Implementation-Checklist.md)): whole-library dedup, optional post-import activate, per-var name≠meta choice, configurable temp dir, encoding-fix as copy modifier, durable copy + import history. Replaces the read-only "Download intake" tab. |

## Amendments (dated)

| # | Date | Amendment | Rationale |
|---|------|-----------|-----------|
| A12 | 2026-07-21 | **Separate `VarVault.Indexer` worker process** owns discovery, one-handle ingestion, catalog mutations, checkpoints, and derived phases. GUI is a read-only catalog client + named-pipe command/status peer. Interactive mutations route to the worker at high priority so there is one process-level SQLite writer. | 5 TB scans must survive GUI close/crash; CPU/RAM isolation; overlapping in-process writers caused lag and contention. Revisit only if IPC cost dominates small libraries — keep protocol versioned. |
| A13 | 2026-07-21 | **Raw-first ingestion:** discover → one-handle extract (central directory + meta + raw deps + signatures/encoding + **representative thumbnail**) → durable `RawStored` → **later** paged dependency resolve / usage / deep analysis. Incomplete rows never authorize dedup/delete/migration. | Avoid holding whole-catalog graphs in RAM; avoid reopening vars for dependency building; I/O once per changed var. |
| A14 | 2026-07-21 | **Representative thumbnail in the same one-handle pass** supersedes IDX-1’s “previews only in Stage 2” for the package representative image only. Non-representative images remain deferred/never. Entry-size + pixel + decompression caps apply. | User I/O constraint: reopen-for-preview doubles random reads on HDD/5 TB libraries. |
| A15 | 2026-07-21 | **Durable scan/phase ledger** (`ScanRun` + per-file ingest state/leases/generation). Dirty packages and phase checkpoints are rows, not in-memory sets. Crash → reclaim expired leases → resume. | Restores sealed M7 durable dirty-queue intent; fixes permanent stale read-model after mid-run crash. |
| D2′ | 2026-07-21 | Thumbnail store = **sharded SQLite** (`thumbnails/thumb_XX.db`, 256 shards by PackageId low byte), not a single `thumbs.db`. | Scale/VACUUM/corruption isolation at 700k+; regenerable cache. Supersedes D2 file layout only. |
| A16 | 2026-07-21 | Potentially unbounded **secondary UI lists** use **server-side numbered paging** with default page size **50** and allowed sizes **25 / 50 / 100**; the primary Library remains the sealed **A9** virtualized ordered-snapshot exception. | Secondary surfaces can grow with the library and must not materialize all rows into Avalonia controls; the primary browser still needs O(1) scrolling rather than a pager. |

---

*No open questions remain. Any future change to a sealed decision is logged here as a dated amendment.*
