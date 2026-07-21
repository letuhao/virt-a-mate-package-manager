# New App — Data Architecture (v2, post-review)

Foundation layer. Stack: **SQLite + FTS5, EF Core 10** ([01-Tech-Stack](./01-Tech-Stack.md)); serves [02-Features](./02-Features.md). This **v2 supersedes the v1 draft**, folding in the 8 root causes and all resolutions from the [adversarial review](./04-Data-Architecture-Review.md), plus four confirmed product decisions:

- **Preset switching = fast directory-swap profiles** (repoint one directory symlink; instant regardless of var count).
- **Gallery = materialized ordered snapshot** (TreeDataGrid keeps O(1) scrollbar random access).
- **Dedup & migration = always propose, never auto-delete/move**; single-copy is a hard gate.
- **Identity matching = app-computed Unicode fold key** (NFC + full case-fold) — the library is heavily non-ASCII (CJK).

---

## 1. Design principles

1. **Never load the whole catalog into memory.** UI binds to virtualized, paged queries over a materialized read model. RAM flat at 70K or 700K.
2. **Identity is the verbatim filename; physical facts live on the physical file.** (R1, R2)
3. **Index everything filtered/sorted/searched**; FTS5 for a curated text blob (not raw paths).
4. **A single-writer recompute pipeline** owns every derived value, with defined transaction boundaries. (R7)
5. **Durability discipline on every physical op**; **one predicate gates every deletion**; **DB + trash are safety infrastructure.** (R4, R5, R6)
6. **Propose, don't auto-destroy.** Migration/dedup/reclaim produce reviewable plans; nothing irreversible runs unattended.
7. **Offline ≠ gone; heuristic ≠ verified.** Offline copies never count as safety; heuristics never trigger destruction.

---

## 2. Identity model: Package (logical) vs VarFile (physical)

- **Package** = logical identity, anchored on the **verbatim base filename** `Creator.Package.Version` (exact token, e.g. `.007` preserved). Holds favorites, tags, usage, presets, dependency *resolution*. Stable across drive moves, dedup, and encoding-fixes.
- **VarFile** = one physical `.var`: a path in a repo, size, fingerprints, health. **One Package → many VarFiles** (duplicates across drives).
- **Matching key** = `IdentityKey`: the identity string after **NFC normalization + full Unicode case-fold** (app-computed, since SQLite `NOCASE` is ASCII-only). All joins/uniqueness/reference-matching use `IdentityKey`; all display uses the verbatim string.
- **Facets** (`Creator`, `PackageName`, `VersionToken` string + `VersionSort` numeric) are parsed **from the filename**, never from `meta.json`. `meta.json`'s own `creatorName`/`packageName` are stored separately (`MetaCreator`/`MetaPackage`) and a divergence is flagged, never used for identity or dependency matching.

---

## 3. Entity model

`PK` = primary key (surrogate `Id` unless noted). Key indexed fields in **bold**. Changes from v1 marked ⟳.

### Storage
**Repository**
- `Id` PK (GUID, stable), `Name`, **`MountPath`**, ⟳`VolumeSerial` (enforced on re-point — mismatch → read-only + prompt)
- `MediaType` (NVMe/SSD/HDD/Network/Removable/Unknown), **`Tier`**, `PriorityInTier`
- `ReadSpeedMBps`, `WriteSpeedMBps`, `BenchmarkedAt`
- `CapacityBytes`, `FreeBytes`, `MinFreeBytes`
- `IsEnabled`, `IsOnline`, `IsReadOnly`, `CreatedAt`, `UpdatedAt`
- ⟳ Registration **rejects** a path equal to or nested within `{vampath}\AddonPackages`, or overlapping another repo.

### Catalog
**Package** — logical identity.
- `Id` PK
- ⟳ **`VarName`** (verbatim `Creator.Package.Version`, unique), ⟳ **`IdentityKey`** (NFC + case-fold, unique, the match key)
- ⟳ `Creator`, `PackageName`, `VersionToken` (string), `VersionSort` (numeric, `long`) — **parsed from filename**
- ⟳ `MetaCreator`, `MetaPackage`, `MetaDivergent` (bool)
- `LicenseType`, `Description`, `ProgramVersion`, `MetaDate` (from the **elected canonical** VarFile)
- **`IsFavorite`**, `FirstSeenAt`, `LastIndexedAt`
- ⟳ `ReverseDependentCount` (direct in-degree, maintained incrementally), `IsFoundational` (high in-degree flag) — see §5.3 / [spike](./05-Perf-Spike-Results.md)
- ⟳ `CanonicalVarFileId` (FK → VarFile, **nullable, `ON DELETE SET NULL`**; re-elected on loss)

**VarFile** — physical instance.
- `Id` PK, **`PackageId`** (FK, nullable — unparsed names), **`RepositoryId`** (FK), **`RelativePath`** (unique w/ RepositoryId)
- `SizeBytes`, `FileMtime`, **`QuickHash`** ⟳(size + head/tail + central-dir offset/count, for freshness)
- ⟳ **`ContentSignature`** (hash of sorted `(rawEntryNameBytes, uncompressedSize, CRC-32)` multiset; **raw bytes**, Zip64-aware, directory entries excluded — the primary dedup key)
- ⟳ `PayloadSignature` (as above, excluding `meta.json`)
- ⟳ `ContentSignatureNoPath` (sorted `(uncompressedSize, CRC-32)` multiset, **paths excluded** — relates original↔encoding-fixed)
- `ContentHash` (full SHA-256, nullable, lazy — only for **verify-before-delete** and portability re-match)
- ⟳ `EncodingHealth` (Ok/NeedsFix/PartiallyBroken/Fixed/Unknown), `DetectedCodepage`, `BrokenEntryCount`, `IntegrityStatus` (Ok/CorruptZip/MissingMeta/BadName)
- ⟳ `FixedFromVarFileId` (FK, nullable — fix lineage), `SupersededByVarFileId` (nullable)
- ⟳ `QuarantineKind` (None/Redundant/Stale/OldVersion/Deleted — recognizes existing `___…___` dirs on import)
- `IndexedAt`

**ContentItem** — ⟳ **keyed on `VarFileId`** (content is physical; copies may differ).
- `Id` PK, **`VarFileId`** (FK), **`Type`** (scenes/looks/clothing/hairstyle/morphs/pose/skin/plugins/assets)
- `EntryPath`, `IsPreset`, `PreviewThumbRef` (→ packed thumbnail store), `Gender` (nullable), `GenderConfidence`

**PackageContentCount** — ⟳ normalized (avoids a schema migration per new content type).
- **`PackageId`** (FK), `Type`, `Count` — derived from the canonical VarFile.

**Dependency** — ⟳ extracted **per VarFile**, resolved **per Package**.
- `Id` PK, **`VarFileId`** (FK — where the edge was harvested; incl. embedded scene/`.vap` refs, not just meta.json)
- **`DependsOnRefKey`** (folded), `DependsOnRefRaw`, `RefKind` (Meta/Embedded/Self)
- **`ResolvedPackageId`** (FK, nullable), `IsVersionSubstituted`, **`IsMissing`**, `ResolvedVia` (Exact/Latest/Closest/Alias)
- `UNIQUE(VarFileId, DependsOnRefKey)`; self-edges (`SELF:`) resolved to the container Package and excluded from missing; unparseable refs **recorded** (`RefKind`/flag), never silently dropped.

**UserSave** — ⟳ NEW (the old `savedepens`). The user's own loose scenes/looks/presets under `{vampath}\Saves`, `Custom\…`.
- `Id` PK, **`Path`**, `Type`, `Mtime`, `LastScannedAt`

**SaveDependency** — ⟳ NEW.
- `Id` PK, **`UserSaveId`** (FK), **`DependsOnRefKey`**, `DependsOnRefRaw`, `ResolvedPackageId` (FK, nullable), `IsMissing`
- Folded into reverse-closure and orphan/reclaim logic so user-needed vars are never "orphans".

### Analyzer
**UsageEvent** — append-only. `Id` PK, **`PackageId`** (FK), **`Timestamp`** (⟳ UTC epoch), `Kind` (Activate/Load/PresetLoad), `Source` (AppObserved/VamLogImport). Events older than the largest window compacted into rollups.

**UsageStat** — materialized per-Package. `PackageId` PK.
- `LastUsedAt`, `UseCountTotal`, ⟳ counts computed **time-relative** (recomputed on schedule + on-read when `ComputedAt` stale, or on-the-fly from indexed events)
- `CentralityScore` (recomputed only on graph change), `Score`, **`Class`** (Hot/Warm/Cold)
- ⟳ `ScoreHistory`/`LastFlipAt` (so `Class` is reproducible after restore — hysteresis isn't path-dependent)
- `IsPinnedHot`, `IsForcedCold`, `ComputedAt`

**MigrationJob** — ⟳ durability + concurrency.
- `Id` PK, **`VarFileId`** (FK), `SourceRepositoryId`, `TargetRepositoryId`
- **`State`** (Planned/Approved/Copying/Verifying/Renaming/Deleting/Done/Failed/Cancelled), `BytesCopied`, `TempPath`, `StartedAt`, `CompletedAt`, `Error`
- ⟳ `UNIQUE(VarFileId) WHERE State NOT IN (Done,Failed,Cancelled)` (one live job per file)

### Activation & presets (directory-swap profiles)
**Profile** — ⟳ NEW. A physical AddonPackages profile directory under `{vampath}\___AddonPacksSwitch ___\{Name}\`.
- `Id` PK, **`Name`** (unique), `DirPath`, `IsActive` (the one `AddonPackages` symlink currently points at), `CreatedAt`, `UpdatedAt`
- Switching = repoint the single `AddonPackages` directory symlink → **instant, O(1)** regardless of var count. Building/editing a non-active profile happens in the background.

**ActivationLink** — ⟳ keyed by the physical file linked, scoped to a profile.
- `Id` PK, **`ProfileId`** (FK), **`VarFileId`** (FK — the specific copy linked; prefer hottest **online** tier), `LinkPath`
- `LinkKind` (Install/Alias/Temp), `AliasedMissingRefKey` (nullable — for renamed alias links naming a missing ref), `LinkSubfolder` (VaM browser grouping)
- `LinkType` (Symlink/Hardlink — hardlink only within one volume), `Reason` (Explicit/DependencyOf/Temp), `RequestedByPresetId` (attribution → safe deactivation reference-counts auto-pulled deps)

**LoadingPreset** — a named var-set, realized as a Profile.
- `Id` PK, **`Name`**, `Description`, `ProfileId` (FK, 1:1), `IsRuleBased`, `RuleJson`, `CreatedAt`, `UpdatedAt`

**PresetMember**
- `PresetId` (FK), ⟳ **`PackageRefKey`** (folded) + `PackageRefRaw`, `ResolutionMode` (Exact/Latest), ⟳ `ResolvedPackageId` (FK, nullable), `ResolvedVersion` (snapshot), `IsVersionSubstituted`, `IsPinned`, `SortOrder`

**VarAlias** — persistent missing-var resolution.
- `Id` PK, ⟳ **`MissingRefKey`** (folded) + `MissingRefRaw`, ⟳ `ResolvedVarName` (string — **portable across machines**), `ResolvedPackageId` (FK, nullable, `ON DELETE SET NULL`)
- `Scope` (Global/Preset), `PresetId` (FK, nullable), `CreatedAt` — precedence: **a present real match outranks an alias.**

### Discovery
**Tag** (`Id`, `Name`, folded `NameKey` unique) · **PackageTag** (`PackageId`, `TagId`)
**Collection** (`Id`, `Name`, `IsRuleBased`, `RuleJson`) · **CollectionMember** (`CollectionId`, `PackageId`)
**ContentItemPref** — ⟳ NEW: per-item favorite/hide (round-trips VaM's `AddonPackagesFilePrefs\*.fav/*.hide`). `Id` PK, **`ContentItemId`** (FK), `State` (Fav/Normal/Hide). `Package.IsFavorite` is a convenience aggregate.

### Search, read model, trash, config
**PackageSearch** — FTS5 over a ⟳ **curated blob** (creator, package, tags, description, content-type words, gender) — **not raw content paths**. External-content; rebuilt after bulk, triggers only for incremental single edits.
**PackageListItem** — ⟳ **materialized read table** (not a VIEW) with denormalized stored aggregates: `PackageId, VarName, Creator, PackageName, VersionToken, PrimaryType, TotalSize, OnlineInstanceCount, TotalInstanceCount, IsSingleCopy, IsFavorite, ActualTierMin, Class, IsActive, HasMissingDeps (direct, materialized bit), PreviewThumbRef, LastUsedAt, AddedAt`. Refreshed per-event by the recompute pipeline.
**OrderedSnapshot** — ⟳ NEW: a lightweight ordered `rowid` list per active `(filter, sort)` so the grid gets O(1) `rows[i]` / scrollbar-jump without deep `OFFSET`.
**TrashItem** — ⟳ NEW: `Id` PK, `OriginalPath`, `TrashPath`, `PackageId` (nullable), `Reason`, `TrashedAt`, `Bytes` — plus a **per-item manifest file stored inside the trash** (restore survives DB loss).
**Setting** — VaM install path (first-class), policies, thresholds, tier definitions.

---

## 4. Relationships (text ER)

```
Repository 1 ──< VarFile >── ManyToOne(nullable) ── Package 1 ──(CanonicalVarFileId 0..1 → VarFile, SET NULL)
VarFile 1 ──< ContentItem ──< ContentItemPref
VarFile 1 ──< Dependency (DependsOnRefKey; ResolvedPackageId → Package, SET NULL)
Package 1 ──< PackageContentCount     Package 1 ── 1 UsageStat     Package 1 ──< UsageEvent
UserSave 1 ──< SaveDependency (→ Package, SET NULL)
Package 1 ──< PackageTag >── Tag       Package 1 ──< CollectionMember >── Collection
Profile 1 ──< ActivationLink (→ VarFile) ;  Profile 1 ── 1 LoadingPreset ──< PresetMember (→ Package, SET NULL)
VarAlias (MissingRefKey → ResolvedVarName / ResolvedPackageId) [Global | per Preset]
VarFile 1 ──< MigrationJob        VarFile 0..1 → FixedFromVarFileId (self)
```
Unenforced-by-FK refs (folded keys): `Dependency.DependsOnRefKey`, `SaveDependency`, `PresetMember.PackageRefKey`, `VarAlias.MissingRefKey`, `ActivationLink.AliasedMissingRefKey` — they may reference packages not present locally; resolution is computed.

---

## 5. Key mechanisms

### 5.1 Identity & fold key
Canonical identity = verbatim base filename. `IdentityKey` = `NFC(identity)` then **full Unicode case-fold** (invariant). Every reference (dependency, preset member, alias, save dep) is folded the same way for matching. Facets parsed from the filename; `VersionSort` is a numeric projection of the version token (with `MetaDate` tie-break for non-monotonic re-uploads). Non-compliant names → `Package = null`, shown in an "unrecognized" projection with an auto-suggested identity when their `ContentSignature` matches a known VarFile.

### 5.2 Fingerprints, dedup & lineage
- `QuickHash` (whole-file, cheap) → freshness (`(size, mtime, central-dir offset/count)` unchanged → skip).
- **`ContentSignature`** over **raw entry-name bytes** + uncompressed size + CRC-32, sorted **multiset**, Zip64-aware, directory entries excluded → primary dedup key (works on mojibake CJK vars, where decoding would be lossy).
- `PayloadSignature` (minus `meta.json`) → near-dup (same content, different meta).
- `ContentSignatureNoPath` (size+CRC, paths excluded) + `FixedFromVarFileId` → relate an encoding-fixed var to its broken original (their paths differ, so the primary signature won't match).
- `ContentHash` (full SHA-256, lazy) → **verify-before-delete** only.
- **Dedup groups strictly within one Package `IdentityKey`.** Cross-identity signature matches (same content, different name) are **report-only**, never delete candidates. Divergent copies of one identity (different `ContentSignature`) surface a **content-conflict**, ranked by `IntegrityStatus`/`EncodingHealth` for canonical election — never auto-pruned.

### 5.3 Dependency graph, closures & resolution
- Edges harvested from `meta.json` **and** embedded scene/`.vap` JSON (tolerant scan); `SELF:` → container package (not missing); unparseable refs recorded.
- Forward/reverse closures via recursive CTEs with **cycle detection + depth cap**. ⚠️ **Proven by the [perf spike](./05-Perf-Spike-Results.md): an on-demand reverse closure of a foundational node takes ~39 s and is forbidden on any interactive path.** Mandatory instead: store a **direct reverse-dependent count** per package (`ReverseDependentCount`, maintained incrementally) for instant "how many depend on X"; flag high-in-degree packages with **`IsFoundational`**; cache full reverse closures **only** for foundational nodes (invalidated on graph edit). "What breaks if I remove X" / safe-delete uses direct dependents + the flag with a **frontier early-out cap** ("used by 40,000+ — foundational, removal unsafe") rather than enumerating; full transitive enumeration is a backgrounded, progress-bared action only for non-foundational nodes on explicit request. `HasMissingDeps` (direct) stored as a materialized bit — never computed transitively per grid row.
- Reverse closure for **safe delete/uninstall** spans `Dependency`, `SaveDependency`, `PresetMember`, `VarAlias`, `ActivationLink`.
- Resolution (`ResolvedPackageId`/`IsMissing`) computed in **one pass** that also applies aliases (real match > alias), writes `ResolvedVia`, and is **incremental**: a new `Creator.Package.v` touches only edges targeting that `(Creator,Package)` via a per-`(Creator,Package)` **"current latest" pointer** (O(1) `latest` deref). Resolution changes emit an **audit event**; presets can **pin** a resolved version so `latest`/closest shifts don't silently change what loads.

### 5.4 The recompute pipeline (single writer)
One serialized writer owns all derived state. Each base-table write enqueues a **dirty set**; the pipeline refreshes the affected `Dependency` resolution, `UsageStat`, `PackageContentCount`, `PackageListItem` rows, and FTS entries — in the **same transaction** as the triggering write where correctness demands it, else via a durable dirty-queue. Window counts are time-relative (never trusted stale). Centrality recomputes only on graph change, not the usage cadence.

### 5.5 Deletion predicate (gates dedup, reclaim, version-prune, intake)
A VarFile may be deleted **only if**: another VarFile of the **same Package `IdentityKey`** is **currently online**, its **full `ContentHash` is computed and equals** the candidate's, and the candidate is not the last online copy. `IsSingleCopy` (from `OnlineInstanceCount`) is a **hard gate** — single-copy/irreplaceable content is excluded from all bulk/auto paths and requires explicit per-item opt-in. "Never loaded" is **not** a deletion signal on its own. Everything routes through **trash**, never hard-delete.

### 5.6 Durability & migration state machine
Every physical move: **temp-write (`.partial`) → `FlushFileBuffers` → cache-bypassed verify (`FILE_FLAG_NO_BUFFERING`) → atomic rename → persist completion**, ordered **persist-state → flush → FS-op → persist-completion**, idempotent on resume. `synchronous=FULL` on any transaction gating a destructive op. One live `MigrationJob` per file; a **free-space reservation ledger** prevents concurrent overfill; real free-space checked immediately before copy against `free − reserved − MinFree`. **Never migrate *to* a removable/network tier**; never delete a source until the target's durability is confirmed by a post-write cache-bypassed re-read. Target VarFile row is inserted only in the same durable transaction as verify-success (after rename); partial/temp files are ignored by indexing. Before any move/delete, **re-point** every `ActivationLink`, `CanonicalVarFileId`, and reference to a surviving copy.

### 5.7 Activation via directory-swap profiles
Each `Profile` is a real directory `___AddonPacksSwitch ___\{Name}\` containing `___VarsLink___` / `___MissingVarLink___` / `___TempVarLink___`. `AddonPackages` is a **directory symlink** to the active profile. **Switching = repoint that one symlink (instant).** A `LoadingPreset` builds/edits its profile's links in the background: resolve members → forward-dependency closure → apply persistent aliases (renamed alias links, `LinkKind=Alias`, `Reason=DependencyOf/Explicit`) → create file symlinks to the hottest **online** VarFile of each package. Reconciliation only removes links the app **provably created** (ownership marker); a file merely on an offline repo is **unavailable, not removed**. Requires Windows Developer Mode (or elevation) to create symlinks; `LinkType` records symlink vs hardlink; cross-volume hardlink is disallowed by the planner.

### 5.8 Read model & gallery scrolling
`PackageListItem` is materialized and refreshed per-event (§5.4). The gallery/TreeDataGrid bind to an **`OrderedSnapshot`** — an ordered `rowid` list for the active `(filter, sort)` — giving O(1) `rows[i]` and true scrollbar-jump. Composite indexes back each sort order (`(Class, LastUsedAt DESC, Id)`, `(Creator, TotalSize DESC, Id)`, …). Counts are debounced and approximate (`~12K`) during typing, exact from the materialized table once settled. Thumbnails come from a **packed store** (few blob files / dedicated thumb DB keyed by PackageId) on the fastest tier, decoded off-thread with scroll-ahead prefetch. `PrimaryType` uses a deterministic precedence (scene > look > clothing > hair > morph > pose > skin > plugin > asset); preview-less types get a placeholder.

### 5.9 Trash & DB safety
Trash = same-volume move where possible (instant, no cross-drive copy), capacity-aware, quota-bounded, **never a silent hard-delete fallback**, never the only copy; each item carries a manifest inside the trash. The **catalog DB** is pinned to stable local storage (refuses to run on a repo/removable volume), `integrity_check` on startup, and **auto versioned backups** run on a schedule and **before every destructive batch**. After any DB restore, a filesystem-truth reconcile runs before any pending job or trash action.

---

## 6. Performance architecture

- **Freshness without opening files:** decide re-index from the **directory entry `(size, mtime)`** alone. Open only changed/new files.
- **Worker-process indexing (A12):** `VarVault.Indexer` is the sole catalog writer. GUI reads paged; commands/status over a versioned named pipe. Coalesce duplicate index requests.
- **Raw-first, one-handle ingest (A13/A14):** discovery streams into a durable ledger; each changed var is opened once (central directory + meta/refs + fingerprints/encoding + representative thumbnail under size/pixel caps). Dependency **resolution** is a later paged SQL phase — never `ToListAsync` of the whole graph during ingest. Parallelize **per physical drive** — degree 1 on HDD, higher on NVMe.
- **Durable dirty/phase state (A15):** dirty package IDs and scan phases are rows; crash after upsert still refreshes derived state on resume.
- **Bulk writes** batched in short transactions via raw SQL where hot; FTS maintained in chunks; `ANALYZE` after bulk; `wal_checkpoint` between batches.
- **WAL discipline:** short-lived UI read transactions; pragmas applied on **every** connection open; interactive mutations prioritized over bulk on the worker command queue.
- **Migrations:** prefer additive nullable columns; run large migrations in the **background with progress**; keep churny tables (`UsageEvent`, `ContentItem`) lean; test against a synthetic 2M-row DB before schema lock.
- **Indexes (initial):** `VarFile(RepositoryId, RelativePath)` unique, `VarFile(PackageId)`, **`VarFile(ContentSignature)`**, **`VarFile(PayloadSignature)`**, `VarFile(ContentSignatureNoPath)`, `VarFile(ContentHash) WHERE NOT NULL` (partial), `Package(IdentityKey)` unique, `Package(Creator, PackageName, VersionSort)`, `Package(IsFavorite)`, `ContentItem(VarFileId)`, `ContentItem(Type)`, `Dependency(VarFileId)`, `Dependency(DependsOnRefKey)`, `Dependency(ResolvedPackageId)`, `SaveDependency(DependsOnRefKey)`, `UsageEvent(PackageId, Timestamp)`, `UsageStat(Class)`, `MigrationJob(VarFileId) WHERE live`, `ActivationLink(ProfileId)`, `ActivationLink(VarFileId)`, `PresetMember(PresetId)`, `VarAlias(MissingRefKey)`, plus one composite per `PackageListItem` sort order.

---

## 7. Portability & storage

- **DB:** one SQLite file on stable local storage (not a repo/removable drive); holds all managed state; auto-backed-up.
- **On disk (not in DB):** the `.var` files (in repos), the **packed thumbnail store**, and **trash** (with per-item manifests).
- **Drive moves:** `Repository.Id`/`VolumeSerial` stable; only `MountPath` changes → VarFiles re-match by `ContentSignature`/`ContentHash`. Catalog (favorites/tags/usage/presets/aliases) survives OS reinstall. Presets/aliases export **by name strings** (portable across machines), re-resolved on import with a reported diff.
- **Import from old varManager:** recognize files already in `___VarRedundant___`/`___StaleVars___`/`___OldVersionVars___`/`___DeletedVars___` (via `QuarantineKind`) and ingest `AddonPackagesFilePrefs` `.fav`/`.hide` into `ContentItemPref`.

---

## 8. Automation policy (confirmed)

**Propose, never auto-destroy.** The analyzer classifies continuously and *proposes* migration/dedup/reclaim **plans** (with sizes, targets, ETA, and a full dry-run preview); the user approves. No file is moved or deleted unattended. Single-copy content is hard-gated out of bulk actions. (Fully-automatic migration remains an explicit opt-in for power users, off by default.)

---

## 9. Open items — RESOLVED (see [10-Decisions-Log](./10-Decisions-Log.md))

- ✅ **Read-path performance validated** by the [spike](./05-Perf-Spike-Results.md) at 1M packages. Faceted COUNT debounced/approximate; reverse-closure precomputed/capped (§5.3).
- ✅ **CJK FTS tokenizer** → **`trigram`** (D1).
- ✅ **"Load more" keyset** → row-value tuple `WHERE (sort, Id) < (?, ?)` (moot for the gallery, which uses OrderedSnapshot).
- ✅ **Hysteresis / windows** → 30d/90d; flip needs ≥15% delta & ≥7 days (D4).
- ✅ **Thumbnail store** → dedicated SQLite `thumbs.db` (D2).
- ✅ **OrderedSnapshot** → per-session in-memory, rebuilt on filter/sort change (D3).
- ✅ **FAT/exFAT/network mtime** → 2 s tolerance window (D5).

*Schema is locked pending the first EF Core migration.*

---

*Next: this v2 is ready for schema lock. Draft the EF Core entity classes + first migration, or drill any §9 item first.*
