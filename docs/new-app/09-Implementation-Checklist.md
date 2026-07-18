# 09 — Implementation Checklist (item-level, evidence-gated)

Granular, testable work items for tracing implementation completeness and QC. **Not** feature/module level — each item is a concrete unit you can verify.

## Rules of use
- **Check `[x]` only when BOTH implemented AND verified with concrete evidence.** No "looks done."
- Append the evidence inline when checking. Evidence types:
  - `T:` automated test name · `M:` manual steps + observed result · `B:` benchmark number · `S:` screenshot/artifact link · `D:` real-data run result
- If an item can't be verified yet, leave it `[ ]` even if code exists.
- Items are grouped by **build phase (slice)**. Phase 0 is foundation; Slices 1–5 match the [roadmap](./03-Data-Architecture.md#12). Cross-cutting safety items are called out where they first apply.

Legend: 🔒 = load-bearing contract (must match spec exactly) · ⚠ = data-loss-sensitive (extra verification required).

---

## Phase 0 — Foundation & infrastructure

### Solution & tooling
- [x] 0.1 Modular solution (Common / Sdk / Domain / Infrastructure / Modules.* / Host / Cli); dependency direction enforced · T: `VarVault.Architecture.Tests` (4 tests pass — Common/Sdk/Domain isolation, modules don't reference each other/infra/host)
- [x] 0.2 .NET 10 on all projects (Directory.Build.props); builds clean · M: `dotnet build` → 0 errors, net10.0
- [x] 0.3 DI container wired via Host composition root (module `Register` into one `IServiceProvider`, `ValidateOnBuild`) · T: `HostCompositionTests` + `dotnet run VarVault.Cli` → "2 module(s) loaded: Repositories, Indexing"
- [ ] 0.4 Serilog configured with file sink + rolling; a startup log line is written (M: log file exists after run) *(packages referenced; wiring pending)*
- [x] 0.5 xUnit tests run; central package management (Directory.Packages.props) · T: `dotnet test` → 16 passed, 0 failed
- *Foundation extras done:* SDK contract boundary (`IModule`/`IModuleContext`/`IPlugin`/`IEventBus`), `Result<T>`/`Guard`/`IClock` kernel, `PackageId` value object (·007 preserved, invalid rejected — tested), `global.json`, `.editorconfig`.

### Database & migrations
- [ ] 0.6 SQLite via EF Core 10; connection opens `WAL` + `synchronous` set per policy (M: `PRAGMA journal_mode` returns wal)
- [ ] 0.7 First EF migration creates the schema; migrate-up on empty DB succeeds (T)
- [ ] 0.8 `PRAGMA foreign_keys=ON` enforced at connection open (T: FK violation throws)
- [ ] 0.9 Startup `PRAGMA integrity_check` runs; corrupt DB is detected and surfaced (M: corrupt a copy → app reports)
- [ ] 0.10 ⚠ DB refuses to open if located on a repo/removable volume (T: path on flagged volume → guarded error)
- [ ] 0.11 App-computed Unicode fold-key function: NFC + full case-fold; ASCII and CJK both covered (T: `Fold("Café")==Fold("café")` folded equal per rule; `Fold("МЕ")==Fold("ме")`)

### Schema — entities & constraints (each = table created + constraints enforced)
- [ ] 0.12 `Repository` table + `VolumeSerial`, tier, priority, capacity fields (T: insert/read round-trip)
- [ ] 0.13 `Package` — unique `IdentityKey`, unique `(Creator,PackageName,VersionSort)`? (note: VersionSort not unique alone) unique `VarName`; `CanonicalVarFileId` nullable ON DELETE SET NULL (🔒 T: deleting canonical VarFile nulls the pointer, does NOT delete Package)
- [ ] 0.14 `VarFile` — unique `(RepositoryId,RelativePath)`; FK PackageId nullable (T)
- [ ] 0.15 `ContentItem` keyed on `VarFileId` (🔒 not PackageId) (T: schema check)
- [ ] 0.16 `Dependency` — unique `(VarFileId,DependsOnRefKey)`; self-edges droppable (T)
- [ ] 0.17 `UserSave` + `SaveDependency` tables exist (T)
- [ ] 0.18 `UsageEvent` (UTC timestamps) + `UsageStat` tables (T: stored value is UTC epoch)
- [ ] 0.19 `MigrationJob` — unique partial index `(VarFileId) WHERE State NOT IN (Done,Failed,Cancelled)` (⚠ T: second live job on same file rejected)
- [ ] 0.20 `Profile`, `ActivationLink`, `LoadingPreset`, `PresetMember`, `VarAlias` tables (T)
- [ ] 0.21 `Tag`/`PackageTag`/`Collection`/`CollectionMember`/`ContentItemPref` tables (T)
- [ ] 0.22 `PackageListItem` materialized table + `TrashItem` + `Setting` (T)
- [ ] 0.23 FTS5 `PackageSearch` virtual table with the chosen **CJK-capable tokenizer** decided (🔒 M: MATCH on space-less CJK returns rows)
- [ ] 0.24 All indexes from data-arch §6 created; `ANALYZE` runs post-bulk (M: `EXPLAIN QUERY PLAN` uses each index)

### Recompute pipeline (single writer)
- [ ] 0.25 Single-writer queue: all writes serialize through one connection; interactive writes prioritized over bulk (T: concurrent favorite-toggle completes while bulk batch runs)
- [ ] 0.26 Dirty-set mechanism: a base-table write enqueues affected derived rows (T: insert VarFile → its Package's PackageListItem row refresh scheduled)

---

## Slice 1 — Repositories, indexing, library browse

### Repository registration & tiering
- [ ] 1.1 Register a repo by folder path; row persisted (M: add repo → appears)
- [ ] 1.2 🔒 Reject repo path equal to or nested within `{vampath}\AddonPackages`, or overlapping another repo (T: each case rejected with message)
- [ ] 1.3 Detect drive media type (NVMe/SSD/HDD/Network/Removable) (M: known drives classify correctly)
- [ ] 1.4 Capture `VolumeSerial` on register; on re-point, mismatch → read-only + prompt (⚠ T: wrong-volume re-point blocked)
- [ ] 1.5 Benchmark read/write MB/s on register (B: numbers within ~20% of a known reference tool)
- [ ] 1.6 Auto-assign tier from benchmark; manual override persists (M)
- [ ] 1.7 Live free/total capacity; refresh on demand (M: matches OS)
- [ ] 1.8 `MinFreeBytes` reserve stored and honored by placement (T)
- [ ] 1.9 Enable/disable repo; disabled excluded from scans (T)
- [ ] 1.10 Offline detection: unplugged repo → `IsOnline=false`, its VarFiles marked unavailable, **not pruned** (⚠ T: offline repo's rows survive a scan)

### Indexing pipeline (see [06](./06-Feature-Specs-Indexing.md))
- [ ] 1.11 Enumerate `*.var` recursively; exclude `___XXX___` dirs and reparse points (T: symlink/junction not indexed)
- [ ] 1.12 Freshness skip by `(size, mtime)` from directory entry **without opening the file** (T: unchanged file not opened — assert via I/O counter/mock)
- [ ] 1.13 🔒 Identity parse: 3-part `Creator.Package.Version`, digit version; else `PackageId=null` → Unrecognized bucket (T: `.007` preserved; `A.B.C.1` → null; 11-digit version no overflow)
- [ ] 1.14 🔒 `IdentityKey` computed; `Creator.Pkg.007` and `creator.pkg.007` collapse to same key (T)
- [ ] 1.15 Facets (Creator/PackageName/VersionToken/VersionSort) parsed **from filename**, never meta.json (T: meta with different names doesn't change identity)
- [ ] 1.16 Read `meta.json`; missing → `IntegrityStatus=MissingMeta` (T)
- [ ] 1.17 Store `MetaCreator`/`MetaPackage` + `MetaDivergent` flag when they differ from filename (T)
- [ ] 1.18 Content classification by prefix+ext rules → per-type counts; matches legacy type table on a fixture corpus (T: fixture var → expected counts)
- [ ] 1.19 `PrimaryType` chosen by fixed precedence (T)
- [ ] 1.20 🔒 `ContentSignature` over sorted `(rawEntryNameBytes, uncompressedSize, CRC-32)` multiset; **raw bytes**, Zip64-aware, dir-entries excluded (T: two zips of identical content diff compression/order → same signature; mojibake var → deterministic signature)
- [ ] 1.21 `PayloadSignature` (excludes meta.json) computed (T)
- [ ] 1.22 `ContentSignatureNoPath` (size+CRC, paths excluded) computed (T)
- [ ] 1.23 Staged: pass-1 (names+meta+deps) makes catalog browsable before pass-2 (previews+signatures) finishes (M/D: browse during index of a real repo)
- [ ] 1.24 Per-physical-drive parallelism: HDD degree=1, NVMe higher (M: two repos on one HDD don't run concurrent reads)
- [ ] 1.25 Bulk writes batched in transactions; FTS triggers disabled during bulk then rebuilt once (T/B: bulk insert rate acceptable)
- [ ] 1.26 Incremental re-index: second scan of unchanged repo opens 0 files (D: real repo, timed, 0 opens)
- [ ] 1.27 Corrupt zip → `IntegrityStatus=CorruptZip`, not indexed as content (T)
- [ ] 1.28 Prune: VarFile whose file vanished (repo confirmed online) removed; offline → kept unavailable (⚠ T both branches)
- [ ] 1.29 Quarantine-dir recognition (`___VarRedundant___` etc.) → `QuarantineKind` set, not treated as live (T)
- [ ] 1.30 Filesystem watch triggers incremental re-index of changed files (M: drop a var → appears)
- [ ] 1.31 D: full index of a real ~5k-var subfolder completes; report throughput + any files flagged corrupt/unrecognized/needs-fix

### Preview extraction & thumbnails
- [ ] 1.32 Sibling `.jpg` extracted for previewable content items (T)
- [ ] 1.33 Thumbnails in a **packed store** (not 700k loose files) keyed by PackageId on fastest tier (M: store is N blob files)
- [ ] 1.34 Representative preview per package by PrimaryType; preview-less types get a placeholder (M)
- [ ] 1.35 Thumbnails decode off UI thread with scroll-ahead prefetch (M: no UI stall on fast scroll)

### Read model & search
- [ ] 1.36 `PackageListItem` populated/refreshed by the recompute pipeline (T: add var → row appears with correct aggregates)
- [ ] 1.37 `OnlineInstanceCount` vs `TotalInstanceCount` distinct; `IsSingleCopy` derived from online count (⚠ T)
- [ ] 1.38 Composite index per sort order; sort query uses index (no temp B-tree) (T: EXPLAIN)
- [ ] 1.39 `OrderedSnapshot` built per (filter,sort); random `rows[i]` is O(1) (B: matches spike ~0.08 ms)
- [ ] 1.40 FTS search returns ranked results incl. CJK (T: search "刘亦菲" hits)
- [ ] 1.41 Faceted count debounced + approximate during typing, exact on settle (M)
- [ ] 1.42 B: read-path perf on real 70k catalog matches the [spike](./05-Perf-Spike-Results.md) envelope (paging <5 ms, scrollbar jump <1 ms)

### Library UI (Slice-1 screens)
- [ ] 1.43 Table view: content-count columns, tier, state, virtualized scroll (no pager) (S)
- [ ] 1.44 Click-header sort with direction indicator; multi-sort (M)
- [ ] 1.45 Gallery view: virtualized thumbnail wall; toggle with table (S)
- [ ] 1.46 Searchable creator combobox: type-filter + keyboard nav on real creator list (M)
- [ ] 1.47 packageName / Installed / Reset filters apply (M)
- [ ] 1.48 Detail panel: metadata, content-preview strip (type filter/loadable/hide-fav), copies, dependencies (S)
- [ ] 1.49 Select-all-matching vs select-visible; persistent selection across scroll (M)
- [ ] 1.50 Empty / loading / partial-index / offline-repo-row / error states each rendered (S each)
- [ ] 1.51 Grid keyboard nav (arrows/space/shift-select) (M)
- [ ] 1.52 Remembered view: filters/sort/columns persist across restart (M)
- [ ] 1.53 Jobs tray shows the live index job with progress + cancel (M: cancel stops it)

---

## Slice 2 — Dependency engine & missing

- [ ] 2.1 Harvest dependency edges from `meta.json` AND embedded scene/`.vap` JSON (tolerant scan) (T: scene-embedded ref captured)
- [ ] 2.2 🔒 `SELF:` refs resolve to container package, excluded from missing (T)
- [ ] 2.3 Unparseable refs recorded (flag), never silently dropped (T)
- [ ] 2.4 `UNIQUE(VarFileId,DependsOnRefKey)`; self-edges dropped from centrality (T)
- [ ] 2.5 🔒 Version resolution: exact / `latest`=highest / closest-newer-else-newest-older; `$` substitution flagged (T: table of cases)
- [ ] 2.6 Per-`(Creator,Package)` "current latest" pointer; `latest` deref O(1) (T)
- [ ] 2.7 Incremental re-resolve on new version: only edges targeting that `(Creator,Package)` touched; audit event emitted (T)
- [ ] 2.8 `IsMissing`/`ResolvedPackageId` written in one pass **including alias application**; `ResolvedVia` set (T)
- [ ] 2.9 🔒 Alias precedence: present real match outranks alias (T)
- [ ] 2.10 Forward closure via recursive CTE with cycle detection + depth cap (T: cyclic graph terminates)
- [ ] 2.11 ⚠ Reverse closure: `ReverseDependentCount` + `IsFoundational` precomputed; NO on-demand enumeration on interactive path (B: foundational-node "impact" query <50 ms, not 39 s)
- [ ] 2.12 Reverse-closure for safe-delete spans Dependency + SaveDependency + PresetMember + VarAlias + ActivationLink (⚠ T: a var needed only by a UserSave is NOT an orphan)
- [ ] 2.13 UserSave scan of `{vampath}\Saves` + `Custom` → SaveDependency edges (T)
- [ ] 2.14 Missing-deps screen lists refs with needed-by counts; scan variants (installed/all/filtered/saves) (M)
- [ ] 2.15 `HasMissingDeps` materialized bit on read model, direct-only (T)

---

## Slice 3 — Activation, presets, aliases

- [ ] 3.1 🔒 Profile = directory under `___AddonPacksSwitch ___`; `AddonPackages` is a directory symlink to active profile (M: filesystem reflects it)
- [ ] 3.2 🔒 Switch preset = repoint the one directory symlink; O(1) regardless of var count (B: switch time flat for 100 vs 5000 vars)
- [ ] 3.3 Symlink creation works under Developer Mode; clear error if unavailable (M)
- [ ] 3.4 ActivationLink keyed by `VarFileId`; picks hottest **online** copy (⚠ T)
- [ ] 3.5 `LinkKind` (Install/Alias/Temp) + `AliasedMissingRef` + `Reason` (Explicit/DependencyOf/Temp) recorded (T)
- [ ] 3.6 Loading preset CRUD; members by name; ResolvedVersion + IsVersionSubstituted + pin (T)
- [ ] 3.7 Activate preset = resolve members → forward closure → apply aliases → create links; missing reported (M/D)
- [ ] 3.8 Dependency-aware activation pulls closure; preview "will pull in N" before apply (M)
- [ ] 3.9 🔒 Persistent aliases (global + per-preset) re-apply automatically on every load — no re-setup (T: switch away and back, alias still applied)
- [ ] 3.10 Alias serialized by **var-name string** (portable); import re-resolves with reported diff (T: export→import on different library shows diff)
- [ ] 3.11 Import/export preset from txt; validate on import, flag unknowns/version mismatches (T)
- [ ] 3.12 Reconcile: links the app created are owned/marked; reconcile never deletes user-made links or real files (⚠ T)
- [ ] 3.13 Rescue baseline: deactivate all → minimal set; game launches (M/D)
- [ ] 3.14 Temp activation auto-cleaned after use (T)
- [ ] 3.15 Deactivation reference-counts DependencyOf links; drops only unneeded ones (T)

---

## Slice 4 — Duplicates, reclaim, encoding fix

### Deletion predicate (⚠ gates ALL delete paths)
- [ ] 4.1 🔒⚠ A VarFile is deletable ONLY if another copy of the **same IdentityKey** is online AND full `ContentHash` verified equal AND it's not the last online copy (T: each precondition individually blocks deletion)
- [ ] 4.2 🔒⚠ Cross-identity ContentSignature matches are report-only, never delete candidates (T: Bob.X.1 vs Alice.X.1 same content → not offered for deletion)
- [ ] 4.3 ⚠ Single-copy hard gate excludes them from all bulk/auto delete paths (T)
- [ ] 4.4 ⚠ Group with any offline member is blocked from dedup deletion (T)

### Duplicates & reclaim
- [ ] 4.5 Dedup grouping by ContentSignature within one identity; fast at scale (B: matches spike ~2 ms)
- [ ] 4.6 Near-dup by PayloadSignature (same content, diff meta) flagged separately (T)
- [ ] 4.7 Download-intake classification: exact/logical/same-name-diff/near-dup/new (T)
- [ ] 4.8 Reclaim wizard aggregates duplicates + cold-on-SSD + never-loaded orphans (single-copy excluded) with size estimates (M)
- [ ] 4.9 Duplicate review: keep-one, ranked by integrity/health; verified-before-delete (M)

### Encoding health & fix (see [06](./06-Feature-Specs-Indexing.md))
- [ ] 4.10 Per-entry codepage detection (GBK/GB18030/Shift-JIS/Big5/EUC-KR) with round-trip validation; `DetectedCodepage`+`BrokenEntryCount` stored (T: fixture broken vars classify correctly, 0 false-positive on healthy)
- [ ] 4.11 CodePagesEncodingProvider registered; raw entry-name bytes captured (T)
- [ ] 4.12 🔒⚠ Fix writes a NEW UTF-8 var (never overwrite in place); temp→validate→atomic rename (T)
- [ ] 4.13 🔒 Fixed var validated against VaM constraints (ZIP+Deflate, UTF-8 flag, no Zip64/data-descriptors, meta present) before preferring it (T)
- [ ] 4.14 ⚠ Original retained (not trashed) until fix confirmed; `FixedFromVarFileId` lineage set (T)
- [ ] 4.15 ⚠ Auto/batch mode flags low-confidence for review, never deletes originals unattended (T)
- [ ] 4.16 Health report grouped by codepage; batch "fix all" (M)
- [ ] 4.17 Optional slimming is separate, off by default (T)

---

## Slice 5 — Analyzer, placement, migration

- [ ] 5.1 UsageEvent appended on every app-performed activate/load (T)
- [ ] 5.2 UsageStat windowed counts computed time-relative (correct the day after) (T: advance clock → counts change)
- [ ] 5.3 UsageEvent compaction of >90d into rollups (T)
- [ ] 5.4 Hot/warm/cold scoring blends recency+frequency+centrality+overrides with hysteresis; `Class` reproducible from stored state after restore (T)
- [ ] 5.5 Placement policy maps Class→tier; misplaced set computed (T)
- [ ] 5.6 Migration planner diffs actual vs target → proposals; single-copy & offline-target excluded (⚠ T)

### Migration durability (⚠ every item data-loss-critical)
- [ ] 5.7 🔒⚠ State machine Planned→Copying→Verifying→Renaming→Deleting→Done, idempotent resume (T: kill at each state, resume correct)
- [ ] 5.8 🔒⚠ Copy to `.partial` temp → `FlushFileBuffers` → cache-bypassed verify (`FILE_FLAG_NO_BUFFERING`) → atomic rename → then delete source (T: verify reads platter not cache)
- [ ] 5.9 🔒⚠ `synchronous=FULL` on any transaction gating a destructive FS op (T)
- [ ] 5.10 ⚠ Free-space reservation ledger; concurrent jobs can't overfill past MinFree (T)
- [ ] 5.11 ⚠ Never migrate TO removable/network tier (T)
- [ ] 5.12 ⚠ Re-point ActivationLink/CanonicalVarFileId/refs to surviving copy BEFORE deleting source (T)
- [ ] 5.13 Interrupted copy leaves temp only; indexing ignores `.partial`; target row inserted only post-verify (T)
- [ ] 5.14 Proposals inbox: all pending migrations/dedup/fixes/stale queue; approve/reject/batch (M)
- [ ] 5.15 🔒 Propose-never-auto default; auto-migrate is explicit opt-in only (T: default config = propose)
- [ ] 5.16 Analytics: space-by-type/creator/tier, wasting-fast/slow-where-hurts, usage trend (M)

---

## Backend engines (no UI — evidence must be `T:`/`B:`, never `S:`)

> These features are pure backend with no screen to "look at". They are the easiest to falsely mark done, so each internal algorithm gets its own item, verified only by test or benchmark.

### Repository profiling engine
- [ ] BE-R1 Benchmark: warm-up then N sequential + random read/write samples on a temp file in the repo; return median MB/s (B: within ~20% of CrystalDiskMark on the same drive)
- [ ] BE-R2 Media-type detection via device query → NVMe/SSD/HDD/Removable/Network (T: known drives classify correctly; unknown → treated as HDD)
- [ ] BE-R3 Capacity/free refresh via `DriveInfo` with a short TTL cache (T: matches OS within tolerance)
- [ ] BE-R4 `VolumeSerial` capture + strict match on re-point (⚠ T: mismatched serial blocks bind)
- [ ] BE-R5 Tier auto-assign thresholds (configurable, e.g. >3000 MB/s→T1, >800→T2, else T3) (T)
- [ ] BE-R6 Add-drive rebalance candidate computation (which vars would move) (T)

### Content classification engine
- [ ] BE-C1 Precompiled rule set (path-prefix + ext → type); NO per-entry regex recompile (T: assert compiled set reused across entries)
- [ ] BE-C2 Per-type counters + `isPreset` flags produced per var (T: fixture var → expected counts)
- [ ] BE-C3 `PrimaryType` by fixed precedence, deterministic (T)
- [ ] BE-C4 Gender inference from path fragments + confidence score (T: female/male/AUTO/futa fixtures)
- [ ] BE-C5 `ContentItem` rows keyed on `VarFileId`; `PackageContentCount` derived from canonical (🔒 T)

### Usage analyzer & classifier
- [ ] BE-A1 Signal ingestion: every app activate/load → `UsageEvent` (UTC) (T)
- [ ] BE-A2 Windowed aggregation (30d/90d) computed time-relative — correct after clock advances with no new events (T)
- [ ] BE-A3 Centrality = reverse-dependency weight; recomputed only on graph change, not usage cadence (T)
- [ ] BE-A4 Score formula = documented weighted blend (recency + frequency + centrality + overrides); known inputs → expected class (T)
- [ ] BE-A5 Hysteresis: `Class` flips only past the sealed threshold; `LastFlipAt`/score history persisted (T)
- [ ] BE-A6 Incremental recompute scope = only packages with new events since `ComputedAt` (T: unchanged packages untouched)
- [ ] BE-A7 Event rollup compaction of >90d events (T)
- [ ] BE-A8 🔒 Classification reproducible from stored state after DB restore / on a second machine (T)

### Placement & migration planner
- [ ] BE-P1 Desired tier = f(class, policy); actual = VarFile→Repo→Tier; diff → misplaced set (T)
- [ ] BE-P2 ⚠ Proposal set excludes single-copy, offline members, and removable/network targets (T)
- [ ] BE-P3 ⚠ Free-space reservation ledger for concurrent-job capacity safety (T)
- [ ] BE-P4 ETA estimate from bytes ÷ target write speed (M)
- [ ] BE-P5 🔒 Propose-only by default; auto-execute is explicit opt-in (T: default config = propose)

### Fingerprint & dedup engine
- [ ] BE-F1 🔒 Central-directory reader: Zip64-aware, raw entry-name bytes, dir-entries excluded (T)
- [ ] BE-F2 Three signatures (Content / Payload / NoPath) computed in one pass (T)
- [ ] BE-F3 Lazy full `ContentHash` computed only for verify-before-delete / portability (T: not computed during normal index)
- [ ] BE-F4 Dedup grouping within one `IdentityKey`; cross-identity matches report-only (🔒 T)

---

## Cross-cutting — safety, ops, portability

- [ ] X.1 ⚠ Never hard-delete: all deletes → trash (same-volume move where possible) (T)
- [ ] X.2 ⚠ Trash quota + capacity-aware; never silent hard-delete fallback; per-item restore manifest inside trash (T: restore works with DB absent)
- [ ] X.3 ⚠ Trash restore returns file to original path + re-index (T)
- [ ] X.4 ⚠ Auto versioned DB backup on schedule AND before every destructive batch (T)
- [ ] X.5 ⚠ After DB restore, filesystem-truth reconcile runs before any pending job/trash action (T)
- [ ] X.6 Undo toast on reversible actions (M)
- [ ] X.7 Confirm-destructive modal: reverse-dep check + single-copy protection shown (M)
- [ ] X.8 Onboarding wizard: add drives → benchmark → index → rescue (M/D end-to-end)
- [ ] X.9 Import-from-old-varManager: recognize quarantine dirs + ingest `.fav`/`.hide` sidecars → ContentItemPref (T)
- [ ] X.10 Portable catalog: move DB + shuffle drive letters → app re-finds vars by hash/volume-serial (⚠ D)
- [ ] X.11 Jobs tray: multiple concurrent jobs, per-job progress + pause/cancel (M)
- [ ] X.12 Activity history: every move/install/delete/fix/alias audited (M)
- [ ] X.13 Settings persist (VaM path, tiers, policies, fix-on-import, preset-extraction defaults) (M)
- [ ] X.14 Command palette (Ctrl-K) global search/actions (M)
- [ ] X.15 Light/dark theme correct on both; WCAG AA contrast on muted text (M: contrast check)
- [ ] X.16 Colorblind-safe: temperature/state encoded by shape/text, not color alone (M)

---

## Scope — SEALED (see [10-Decisions-Log](./10-Decisions-Log.md))
- Hub browsing — **OUT** of v1 (reversible; future optional plugin). No items.
- Load-into-VaM (`loadscene.json` + in-game plugin) — **OUT** of v1 (reversible). No items.
- Scene/preset extraction · MMD loader · var packaging — **OUT**. No items.

---

*Convention: as items complete, replace `[ ]` with `[x]` and append `· T:/M:/B:/S:/D: <evidence>`. Un-evidenced code stays unchecked. Re-run this list as the QC gate before calling any slice "done."*
