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

---

*No open questions remain. Any future change to a sealed decision is logged here as a dated amendment.*
