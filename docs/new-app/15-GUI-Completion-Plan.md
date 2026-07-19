# 15 — GUI Completion Plan (prototype → real app)

**Status: the backend is largely built; the GUI is a stub.** The implementation checklist
([09](09-Implementation-Checklist.md)) is 206/206, but those items proved *engines + view-models + unit
tests* — not a running application. This document compares the sealed prototype
([mockups/prototype.html](mockups/prototype.html)) screen-by-screen against what the Avalonia app actually
renders, and lays out a plan to build the real GUI.

## The gap in one paragraph

The prototype is a **14-screen shell** (rail nav + top bar + jobs panel + log dock + global search + ~10
modals). The app ships **one window** — a *reduced* Library screen — and its composition root
([AppHost.cs](../../src/VarVault.App/Composition/AppHost.cs)) resolves exactly **one** service
(`ILibraryQueryService`). Twelve screen view-models exist but are **never instantiated at runtime**
(only unit tests construct them); six prototype screens have **no view-model at all**; and six screens'
engines live in Domain/Infrastructure with **no SDK interface**, so the App — which references only
Common + Sdk + Host — cannot bind them. Net: the algorithms are real, the app that exposes them is not.

## Evidence

| Claim | Reality | Source |
|---|---|---|
| App is a multi-screen shell | One `Window`, one screen (Library) | [MainWindow.axaml](../../src/VarVault.App/Views/MainWindow.axaml) — 122 lines, no nav |
| Screen view-models are wired | Only `LibraryViewModel` is; 12 others never constructed in prod | [AppHost.cs](../../src/VarVault.App/Composition/AppHost.cs) resolves only `ILibraryQueryService` |
| `MainWindowViewModel` hosts the screens | Exposes only `Library` + `Title` | [MainWindowViewModel.cs](../../src/VarVault.App/ViewModels/MainWindowViewModel.cs) — 11 lines |
| Screens can bind their backend | App references Common+Sdk+Host only; 6 engines have no SDK facade | [VarVault.App.csproj](../../src/VarVault.App/VarVault.App.csproj) |
| `.axaml` views exist per screen | Only `MainWindow.axaml` + `App.axaml` exist | `find src/VarVault.App -name *.axaml` |

## Screen-by-screen comparison

Legend — **View**: an `.axaml` exists. **VM**: a view-model exists. **SDK**: an App-bindable service
exists. **Engine**: the Domain/Infrastructure logic exists.

| # | Prototype screen | View | VM | SDK facade | Engine | Verdict |
|---|---|:--:|:--:|:--:|:--:|---|
| 1 | **Dashboard** (tier storage, needs-attention, reclaimable, classification, recent, quick actions) | ✗ | ✗ | partial | ✓ | Build view+VM; compose over `IAnalyticsService`/`IRepositoryService`/`IMissingDepsQuery`/`IActivityLog` (+ small summary facade) |
| 2 | **Library** (rail views/tags/collections/tools, facet chips, per-type count cols, ops/bulk bar, detail: previews strip + deps + copies) | ▲ | ✓ | ▲ | ✓ | Exists but **reduced** — see "Library deltas" below |
| 3 | **Loading presets** (list, member table, switch/deactivate, edit/diff/export) | ✗ | ✗ | ✓ `IPresetService`/`IActivationService` | ✓ | Build view+VM; profile-switch needs `IVamProfileService` surfaced via SDK |
| 4 | **Tiering & migration** (class counts, misplaced proposals, tabs) | ✗ | ✗ | ✗ | ✓ `MigrationPlanner`/`RebalancePlanner`/`IUsageAnalyzer` | Build SDK facade + view+VM |
| 5 | **Duplicates & reclaim** (reclaim cards, exact-dup groups, tabs) | ✗ | (`ReclaimVM`,`DedupReviewVM` placeholders) | ✗ | ✓ `DedupGrouping`/`DeletionPredicate` | Build SDK facade + views; wire the placeholder VMs |
| 6 | **Analytics** (space by type/creator, wasting-fast, usage chart) | ✗ | ✓ `AnalyticsVM` | ✓ `IAnalyticsService` | ✓ | Build view; wire VM (backend ready) |
| 7 | **Proposals & review** (approve/reject cards, tabs) | ✗ | ✓ `ProposalsVM` placeholder | ✗ | ✓ (migration+dedup+encoding+stale) | Build SDK proposal facade + view |
| 8 | **Health & fix** (encoding/integrity/missing-meta tabs, group cards) | ✗ | ✓ `HealthReportVM` (delegate only) | ✗ | ✓ `EncodingHealthEngine`/`EncodingFixCoordinator` | Build SDK facade + view |
| 9 | **Missing deps** (missing-ref table, alias mapping) | ✗ | ✓ `MissingDepsVM` | ✓ `IMissingDepsQuery` | ✓ | Build view; **backend fix needed** (see correctness gaps) + alias write path |
| 10 | **Repositories** (repo cards: tier/benchmark/capacity/online, actions) | ✗ | ✗ | ✓ `IRepositoryService` | ✓ | Build view+VM |
| 11 | **Trash & backup** (trash table restore/purge, backups tab) | ✗ | ✗ | ✗ | ✓ `ITrashService`/`SqliteDatabaseBackup` | Build SDK facade + view+VM |
| 12 | **Activity history** (filter, audit table) | ✗ | ✓ `ActivityVM` | ✓ `IActivityLog` | ✓ | Build view; wire VM (backend ready) |
| 13 | **Settings** (general/tiers/automation/import/advanced tabs) | ✗ | ✗ | ✓ `ISettingsService` | ✓ | Build view+VM |
| — | **Shell chrome**: rail nav (grouped, badges), top bar, jobs panel, log dock, Ctrl-K command palette | ✗ | (`CommandPaletteVM`,`JobsVM`) | ✓ `IJobQueue` | ✓ | Build the shell — the spine everything docks into |
| — | **Modals**: onboarding wizard, add-repo, migrate-plan, fix-encoding, alias, confirm-delete, edit-preset, dupe-review, rescue, var-detail, undo toast | ✗ | (`OnboardingVM`,`ConfirmVM`,`UndoToastVM`) | mixed | ✓ | Build dialog host + per-modal views |

### Library screen deltas (screen #2 is present but partial)

Current window has: creator autocomplete, search box, favorites toggle, sortable Name/Creator/Size/Class
headers, table + gallery lists, a thin detail panel (name/creator/type/class/copies + single-copy/missing
flags), state overlays, approximate-count marker. **Missing vs prototype:**
- Left rail: saved views (All/Favorites/Active/Single-copy/Unrecognized/Recent), Tags, Collections, Maintenance tools (rebuild symlinks, batch-fix, find-dupes, find-stale), Dependency-analysis actions.
- Facet bar: `packageName` filter, Installed chip, type chips, Tier chips, "＋ Filter", row-range indicator, richer sort menu.
- Table: per-type count columns (Sc/Lk/Cl/Hr/Pl/Mo), Tier, Copies, Dep-count, State text, **Fix Var** column.
- Ops/bulk bar: select-all-matching, **Install/Uninstall/Delete/Move-to-subfolder/Add-to-preset/Fix-encoding/Export-txt/Install-from-txt**.
- Detail panel: preview hero, action buttons (install/favorite/locate/full-detail), **content-preview strip**, **dependencies list** (ok/sub/missing), **copies list**.

## New backend tasks catalog (algorithms + wiring)

The item tables below reference these tasks by id. Each is a **new SDK interface** (App-bindable) plus its
Infrastructure implementation + DI registration, wrapping engines that already exist. Rule: the App binds
only SDK; every write goes through `IWriteQueue`; every long op runs as an `IJobQueue` job.

**BE-N0 — Index orchestration + trigger (highest priority; makes everything else non-empty).**
Extend the Indexing module with an orchestrator and wire a trigger.
- *Algorithm:* `IndexAllAsync()` → for each enabled+online repository (`IRepositoryService.ListAsync`) call
  `IndexRepositoryAsync`; then `IDependencyResolver.ResolveAllAsync()`; then `IUsageAnalyzer.RecomputeAsync()`.
  Run inside one `IJobQueue` job reporting staged progress (enumerate → inspect → resolve → usage).
- *Fix:* `EfCatalogStore.RefreshOneAsync` must stop setting `HasMissingDeps=false` unconditionally —
  recompute it from the package's direct deps (`Dependency.IsMissing` for the canonical var), or leave the
  resolver-owned value untouched on refresh.
- *Wiring:* new SDK `IIndexOrchestrator.IndexAllAsync`/`IndexRepositoryAsync(id)`; register scoped; call
  from the shell's "Index now" command and after `RegisterAsync`.

**BE-N1 — `IDashboardService` (read facade).** Aggregates the dashboard tiles.
- *Algorithm:* compose existing queries: totals (`COUNT`, `SUM(TotalSize)` over `PackageListItem`), per-tier
  used/free (`IRepositoryService.ListAsync` capacities), hot/warm/cold counts (`GROUP BY Class`), reclaimable
  = dup bytes (BE-N4) + cold-on-fast bytes (BE-N2) + never-loaded-orphan bytes, attention list (proposals
  count BE-N8, encoding count BE-N5, missing-deps count `IMissingDepsQuery`, offline repos).
- *Wiring:* SDK `IDashboardService.GetSummaryAsync()` → `DashboardSummary` record; scoped EF impl.

**BE-N2 — `ITieringService` (SDK) over `PlacementPolicy`/`MigrationPlanner`/`RebalancePlanner`/`IUsageAnalyzer`.**
- *Algorithm:* class counts = `GROUP BY Class`; **misplaced** = for each package, `PlacementPolicy.IsMisplaced(Class, ActualTierMin)`; build `MigrationCandidate`s and `MigrationPlanner.Plan(candidates, targetTierIsSafe)` where `targetTierIsSafe` checks free space via `FreeSpaceLedger`; rebalance = `RebalancePlanner.CandidatesForNewTier`.
- *Wiring:* SDK `ITieringService.ClassCountsAsync()`, `MisplacedAsync()`, `BuildPlanAsync(...)` → returns a `MigrationPlanDto` (id persisted for the runner). Register scoped.

**BE-N3 — `IMigrationService` (SDK) over `MigrationRunner`/`IDurableFileMover`.** Runs an approved plan.
- *Algorithm:* persist plan → `MigrationRunner.RunAsync(jobId)` executes each move copy→verify→rename→delete
  (source trashed via `ITrashService`, never hard-deleted), crash-safe/resumable; single-copy excluded.
- *Wiring:* SDK `IMigrationService.RunPlanAsync(planId)` as an `IJobQueue` job with copy/verify/rename/delete
  progress stages; writes through `IWriteQueue`.

**BE-N4 — `IReclaimService` (SDK) over `DedupGrouping`/`DeletionPredicate`.**
- *Algorithm:* `DedupGrouping.Analyze(facts)` → exact-dup groups (same `IdentityKey`+`ContentSignature`),
  cross-identity matches (report-only), reclaim buckets; per group, keep one and for the rest evaluate
  `DeletionPredicate.Evaluate(candidate, identityGroup)` (requires full `ContentHash` — compute lazily via
  `Sha256FileHasher` before delete); approved deletes → `ITrashService.TrashAsync`.
- *Wiring:* SDK `IReclaimService.ExactGroupsAsync()`, `ReclaimBucketsAsync()`, `TrashRedundantAsync(keepId, trashIds[])`; reclaim run as a job.

**BE-N5 — `IHealthService` (SDK) over `EncodingHealthEngine`/`EncodingFixCoordinator`.**
- *Algorithm:* encoding groups = `GROUP BY DetectedCodepage` over `VarFile` where `EncodingHealth!=Ok`;
  integrity = `IntegrityStatus=CorruptZip`; missing-meta = no `meta.json`. Fix = `EncodingFixCoordinator.FixAsync(brokenVarFileId, outputPath)` (writes new UTF-8 var, validates it loads, keeps original), batch over a group.
- *Wiring:* SDK `IHealthService.EncodingGroupsAsync()`, `IntegrityAsync()`, `FixAsync(varFileId)`, `FixGroupAsync(codepage)` as a job.

**BE-N6 — `ITrashQueryService` (SDK) over `ITrashService` + `SqliteDatabaseBackup`.**
- *Algorithm:* `ITrashService.ListAsync()` for trash entries; restore = `RestoreAsync(trashId)`; purge =
  hard-delete trashed file; backups list = enumerate backup files, restore = `SqliteDatabaseBackup`.
- *Wiring:* SDK `ITrashQueryService.ListAsync/RestoreAsync/PurgeAsync/ListBackupsAsync/BackupNowAsync`.

**BE-N7 — Profile switch: extend `IActivationService` (or new `IProfileService`) over `IVamProfileService`.**
- *Algorithm:* `ListProfiles(vamRoot)`, `SwitchTo(vamRoot, name)` = repoint the single `AddonPackages`
  symlink (O(1)); `vamRoot` from `ISettingsService[vam.path]`. Preset "Switch" = build links (BE existing
  `BuildProfileLinksAsync`) into the profile dir, then `SwitchTo`.
- *Wiring:* SDK `IProfileService.ListAsync/SwitchToAsync/ActiveAsync`.

**BE-N8 — `IProposalService` (SDK) aggregating tiering + dedup + encoding + stale proposals.**
- *Algorithm:* build proposals = rebalance (BE-N2 misplaced/full-tier), dedup (BE-N4 groups), encoding
  (BE-N5 high-confidence groups), stale (not-latest via `IDependencyGraph.CurrentLatestAsync` AND
  `ReverseDependentCount=0`). Approve dispatches to the matching runner (BE-N3/N4/N5); reject records.
- *Wiring:* SDK `IProposalService.ListAsync(filter)`, `ApproveAsync(id)`, `RejectAsync(id)`.

**BE-N9 — `IPackageDetailQuery` (SDK) for the var-detail modal + Library detail panel.**
- *Algorithm:* identity/foldkey/license/size/class from `Package`+`PackageListItem`; **depended-on-by** =
  `Package.ReverseDependentCount`; **forward+reverse closure** via `IDependencyGraph.ForwardClosureAsync`
  (+ a reverse query); **content items** = `ContentItem` rows of the canonical var (type + entry path +
  preview via `IThumbnailStore`); **copies & lineage** = `VarFile` rows (tier/path/size) + `FixedFromVarFileId` chain.
- *Wiring:* SDK `IPackageDetailQuery.GetAsync(packageId)` → `PackageDetail` (deps[], content[], copies[]).

**BE-N10 — `ILibraryActionService` (SDK) for Library ops-bar bulk actions.**
- *Algorithm:* Install/Uninstall = activation link create/remove for the active profile
  (`IActivationService`); Move-to-subfolder = `IDurableFileMover` within repo + catalog path update;
  Add-to-preset = `IPresetService.AddMemberAsync`; Fix-encoding = BE-N5; Export/Install-from-txt =
  `PresetTextFormat`/`PresetImporter`. All bulk ops respect selection or select-all-matching
  (`GetOrderedIdsAsync`), run as jobs, delete via trash + `DeletionPredicate`.
- *Wiring:* SDK `ILibraryActionService.InstallAsync/UninstallAsync/MoveAsync/AddToPresetAsync/ExportTxtAsync/ImportTxtAsync`.

**BE-N11 — `IAliasService` (SDK) over the `VarAlias` entity.** Resolve missing deps to owned packages.
- *Algorithm:* set alias (missingRef → ownedPackageId, scope Global | preset) persisted to `VarAlias`; on
  resolve/activation the alias substitutes the missing ref; list aliases; the resolver applies aliases before
  marking a ref missing.
- *Wiring:* SDK `IAliasService.SetAsync(missingRef, ownedRef, scope)`, `ListAsync()`, `RemoveAsync(id)`;
  `EfDependencyResolver` must consult aliases (currently it does not — verify + wire).

**BE-N12 — Library facets: tags, collections, saved views, extra filters.**
- *Algorithm:* saved views = predefined `LibraryQuery` presets (Favorites, Active-in-game via
  `ActivationLink`, Single-copy via `IsSingleCopy`, Unrecognized via `PackageId==null` varfiles, Recently
  added via `AddedAt`). Tags/Collections = `Tag`/`PackageTag`/`Collection`/`CollectionMember` entities
  (exist) → new `ITagService`/`ICollectionService`. Extra filters: `packageName`, Installed, type chips,
  tier chips → extend `LibraryQuery` (add `PackageName`, `InstalledOnly`, `Types[]`, `Tiers[]`).
- *Wiring:* extend `ILibraryQueryService`/`LibraryQuery`; add `ITagService`, `ICollectionService`.

**BE-N13 — Onboarding/add-repo flow.** Uses existing `IRepositoryService` (Register/Benchmark/List) + BE-N0.
- *Algorithm:* add folder → `RegisterAsync` (detects media type + `BenchmarkAsync` → suggested tier) →
  optional rebalance (BE-N2) → `IndexAllAsync` (BE-N0). Reserve-free-space persisted per repo.
- *Wiring:* already-SDK Register/Benchmark; add `reserveBytes` to request.

**BE-N14 — Command palette / global search.** `LibraryQuery.SearchText` (FTS) exists for packages; add an
in-app action registry (navigate + commands) filtered client-side. No new persistence.

## Item-level backend mapping

Legend — **✅** bind existing SDK · **🔷** extend existing SDK (small) · **⚙ BE-Nx** new backend task above.
Every numbered row is a concrete item from [prototype.html](mockups/prototype.html) — controls, data
fields, table columns, stats. Repeated example rows are counted once as their column/field set.

### Item census (ground truth ≈ 460 items)

| Area | Items | Area | Items |
|---|--:|---|--:|
| Shell rail nav | 20 | Proposals | 39 |
| Top bar + jobs panel | 20 | Health & fix | 19 |
| Log dock | 3 | Missing deps | 8 |
| Dashboard | 40 | Trash & backup | 12 |
| Library | 78 | Activity history | 5 |
| Repositories | 42 | Settings | 11 |
| Loading presets | 16 | Modals (×11) | 91 |
| Tiering & migration | 23 | Analytics | 15 |
| Duplicates & reclaim | 22 | **GRAND TOTAL** | **≈ 464** |

Coverage rule: **every item below must resolve to a ✅/🔷/⚙ backend** — an unmapped item is a hole.

### Shell rail nav (20)
Nav buttons (client-side nav to each screen): 1 Dashboard · 2 Library · 3 Loading presets · 4 Tiering &
migration · 5 Duplicates & reclaim · 6 Analytics · 7 Proposals · 8 Health & fix · 9 Missing deps · 10
Repositories · 11 Trash & backup · 12 Activity history · 13 Settings. Group labels: 14 Browse · 15
Optimize · 16 Problems · 17 System. Badges: 18 Proposals `7` [⚙BE-N8 count] · 19 Health `725` [⚙BE-N5
count] · 20 Missing `1.2k` [✅ `IMissingDepsQuery` count].

### Top bar + jobs panel (20)
Top bar: 1 global search "Search everything… (Ctrl K)" [⚙BE-N14] · 2 "⛑ Rescue" button [✅
`RescueAsync`] · 3 "⟳ Jobs" button [✅ `IJobQueue.Active`] · 4 Jobs unread dot [✅ `IJobQueue`] · 5 "+ Add
repository" [✅ `RegisterAsync` → m-addrepo] · 6 theme toggle ◐ [client-side]. Jobs panel: 7 header · 8
"pause all" link [✅ `IJobQueue` cancel-all]. Job row ×3 — each has: 9/13/17 title (Indexing / Migrating /
Batch-fix) [✅ `JobHandle.Name`] · 10/14/18 cancel × [✅ `JobContext.Cancellation`] · 11/15/19 progress bar
[✅ `IProgressSink`] · 12/16/20 sub-line (counts/stage/ETA) [✅ `JobHandle.Progress`].

### Log dock (3)
1 indexing status "Indexing T3 18,204/26,455 stage 2" [✅ `IJobQueue`] · 2 "selected 3" [client-side
selection] · 3 "Storage T1 70% · T2 95% · T3 39%" [⚙BE-N1 tier %].

### Dashboard — screen 1 (40)
Header: 1 stat "69,660 packages · 4.3 TB · 4 repos · 1,847 active" [⚙BE-N1] · 2 "Setup wizard" [→m-onboard,
BE-N13] · 3 "⛑ Rescue" [✅ `RescueAsync`].
**Storage-by-tier card** (4 title): per tier T1/T2/T3 — 5/8/11 label+media, 6/9/12 used/total, 7/10/13 bar%
[✅ `IRepositoryService.ListAsync` capacity]; 14 "⚠ T2 near full — rebalance 190 GB" link [⚙BE-N2].
**Needs-attention card** (15 title): 16 "7 proposals" [⚙BE-N8] · 17 "684 vars can't load" [⚙BE-N5] · 18
"1,203 missing deps" [✅ `IMissingDepsQuery`] · 19 "Archive USB offline" [✅ `IRepositoryService`].
**Reclaimable card** (20 title): 21 big "590 GB" · 22 dup 312 GB · 23 cold-on-SSD 190 GB · 24 orphans 88 GB
[⚙BE-N4 + BE-N2] · 25 "Open reclaim wizard →" [nav].
**Classification card** (26 title): 27 stacked bar · 28 hot 8,410 · 29 warm 19,240 · 30 cold 42,010 [⚙BE-N1
GROUP BY Class] · 31 "tune →" [nav].
**Recent-activity card** (32 title): 33/34/35 three log lines [✅ `IActivityLog.GetRecentAsync`] · 36 "full
history →" [nav].
**Quick-actions card** (37 title): 38 "Switch loading preset" [⚙BE-N7] · 39 "Add repository" [✅ Register] ·
40 "Browse library" [nav].

### Library — screen 2 (78)
**Rail (18).** Saved views (+counts): 1 All · 2 Favorites [✅ `FavoritesOnly`] · 3 Active-in-game [⚙BE-N12
`ActivationLink`] · 4 Single-copy [✅ `IsSingleCopy`] · 5 Unrecognized names [⚙BE-N12 `PackageId==null`] · 6
Recently added [⚙BE-N12 `AddedAt`]. Tags: 7 header · 8 "New tag" + · 9/10 tag rows [⚙BE-N12 `ITagService`].
Collections: 11 header · 12 collection row [⚙BE-N12 `ICollectionService`]. Maintenance: 13 Rebuild symlinks
[✅ `IActivationService`] · 14 Batch-fix encoding (badge) [⚙BE-N5] · 15 Find duplicates/reclaim [⚙BE-N4] ·
16 Find stale/old (badge) [⚙BE-N2]. Dep-analysis: 17 Scan missing installed/all [✅ `ResolveAllAsync`] · 18
Scan my Saves / Analyze VaM log [✅ `UserSaveScanner` · minor new log-parse].
**Facet bar (11).** 19 Creator combobox searchable+counts [✅ `GetCreatorsAsync`, 🔷 counts] · 20 packageName
filter [🔷 `LibraryQuery.PackageName`] · 21 Installed chip [🔷 `InstalledOnly`] · 22 Reset [client] · 23
type chip "All types" [🔷 `Types[]`] · 24 tier chip "T1·T2" [🔷 `Tiers[]`] · 25 "＋ Filter" [client] · 26
Table toggle · 27 Gallery toggle [✅ `ToggleViewMode`] · 28 row-range "1–48 of 69,660" [✅ `TotalCount`] · 29
Sort menu [✅ `LibrarySort`, 🔷 DependentCount].
**Table columns (16).** 30 select-all checkbox · 31 Package (+installed ● dot) · 32 Creator · 33 Sc · 34 Lk
· 35 Cl · 36 Hr · 37 Pl · 38 Mo (per-type counts [🔷 `PackageContentCount` projection]) · 39 Size · 40 Tier
(+temp dot) · 41 Cop (copies) · 42 Dep count [🔷] · 43 State ok/missing/needs-fix · 44 Fix-Var col [⚙BE-N5] ·
45 Detail button [→m-vardetail].
**Gallery card (8).** 46 thumb/letter [✅ `IThumbnailStore`, BE-N9] · 47 tier pill · 48 crit/fix badge · 49
installed tag · 50 name · 51 temp dot · 52 type · 53 size [✅ `PackageListEntry`].
**Ops bar (10).** 54 "3 selected" [client] · 55 "select all 12,431 matching" [✅ `GetOrderedIdsAsync`] · 56
Install · 57 Uninstall [⚙BE-N10] · 58 Delete [⚙BE-N10 + `DeletionPredicate` → m-confirm] · 59 Move to
subfolder [⚙BE-N10] · 60 Add to preset [✅ `AddMemberAsync`] · 61 Fix encoding [⚙BE-N5] · 62 Export → txt ·
63 Install from txt [⚙BE-N10].
**Detail panel (15).** 64 hero/preview · 65 identity name · 66 meta (creator·type·copies·size) [✅
`PackageListEntry`/⚙BE-N9] · 67 tags row (hot/T1/installed/favorite) · 68 Install btn [⚙BE-N10] · 69
Favorite btn [🔷 favorite toggle] · 70 Locate btn [open path] · 71 Open full detail [→m-vardetail] ·
previews: 72 type filter · 73 "Loadable only" · 74 Hide/Fav [⚙BE-N9 + content-pref] · 75 preview strip
[⚙BE-N9 `IThumbnailStore`] · 76 Dependencies list ok/sub/missing [⚙BE-N9] · 77 "resolve via alias" [⚙BE-N11]
· 78 Copies list tier/path/size [⚙BE-N9].

### Repositories — screen 10 (42)
Header: 1 "Review rebalance plan" [⚙BE-N2] · 2 "+ Add repository" [✅ `RegisterAsync` → m-addrepo].
**Per repo card ×4** (T1 Samsung, T2 WD, T3 Seagate, offline USB) — each: 3/12/21/30 tier badge · 4/13/22/31
name · 5/14/23/32 online/offline/near-full tag [✅ `RepositoryInfo`] · 6/15/24/33 media·speed·var-count line
[✅ `RepositoryInfo` + 🔷 var-count] · 7/16/25/34 capacity bar · 8/17/26/35 used/total · 9/18/27/36 reserve
[✅ capacity] · 10/19/28/37 SMART health / "reconnect to index" [🔷 SMART minor · ✅ online] · action buttons
11/20/29/38 Re-benchmark [✅ `BenchmarkAsync`] · 39 Tier ▾ [🔷 set-tier] · 40 Edit [✅
`SetEnabledAsync`/`RepointAsync`] · 41 Rebalance… (T2) [⚙BE-N2]. 42 offline card "reconnect to index" note
[✅ online flag]. *(Action buttons repeat per card; counted as the distinct set 11,39,40,41.)*

### Loading presets — screen 3 (16)
Header: 1 "Import from txt…" [⚙BE-N10] · 2 "+ New preset" [✅ `CreateAsync` → m-preset].
Left list: 3/4/5 preset rows +member counts [✅ `ListAsync`]. Detail: 6 title · 7 "active in game" tag [✅
`ActivationPreview`] · 8 Edit [→m-preset] · 9 Diff… [🔷 preset diff] · 10 Export txt [⚙BE-N10] · 11 infobox
"241 pkgs → +38 deps · 3 aliases · 1 missing" [✅ `ActivationPreview` + ⚙BE-N11]. Member table cols: 12
Package · 13 Resolution (exact/sub/alias) · 14 State (ready/substituted/aliased/missing) [✅
`PreviewActivationAsync` + ⚙BE-N11]. Actions: 15 "Switch to this preset" [⚙BE-N7] · 16 "Deactivate all" [✅
`DeactivateAsync`/`RescueAsync`].

### Tiering & migration — screen 4 (23)
Header: 1 "Simulate policy…" [⚙BE-N2] · 2 "Review migration plan" [⚙BE-N3 → m-migrate]. Tabs: 3 Overview · 4
Lifecycle rules · 5 Placement policy · 6 Stale/old-versions [⚙BE-N2 + settings]. Class cards ×3: 7/10/13
label · 8/11/14 number · 9/12/15 bar (hot/warm/cold) [⚙BE-N1/N2]. Misplaced table cols: 16 Package · 17
Class · 18 Now-on · 19 Suggested · 20 Size · 21 Why (reason) · 22 Plan… btn [⚙BE-N2 `IsMisplaced`+reason →
m-migrate] · 23 card title "Misplaced — proposals awaiting approval".

### Duplicates & reclaim — screen 5 (22)
Tabs: 1 Reclaim space · 2 Exact duplicates · 3 Near-duplicates [⚙BE-N4 `PayloadSignature`] · 4 Download
intake [⚙BE-N4 `IntakeClassifier`]. Reclaim cards ×3: 5/9/13 label · 6/10/14 stat · 7/11/15 sub-line ·
8/12/16 btn (dup 312 GB / cold-on-SSD 190 GB / orphans 88 GB) [⚙BE-N4 + BE-N2]. Exact-dup group table: 17
card title · 18 Package (content-identical) · 19 Copies · 20 Locations · 21 Reclaim · 22 Review btn [⚙BE-N4
`DedupGrouping` → m-dupe].

### Tiering & migration (screen 4)
| Item | Kind | Backend |
|---|---|---|
| Tabs: Overview / Lifecycle rules / Placement policy / Stale-old-versions | nav | ⚙ BE-N2 (+ settings for rules) |
| Class counts hot/warm/cold + bars | data | ⚙ BE-N1/BE-N2 |
| Misplaced table: package, class, now-on, suggested, size, why, Plan… | data/action | ⚙ BE-N2 (`IsMisplaced` + reason) |
| "Simulate policy…" / "Review migration plan" | action | ⚙ BE-N2 · ⚙ BE-N3 (modal m-migrate) |

### Duplicates & reclaim (screen 5)
| Item | Kind | Backend |
|---|---|---|
| Tabs: Reclaim / Exact duplicates / Near-duplicates / Download intake | nav | ⚙ BE-N4 (near = `PayloadSignature`; intake = `IntakeClassifier`) |
| Reclaim cards: dup 312 GB / cold-on-SSD 190 GB / orphans 88 GB (excl single-copy) | data | ⚙ BE-N4 (+ BE-N2 cold) |
| Exact-dup group table: package, copies, locations, reclaim | data | ⚙ BE-N4 (`DedupGrouping`) |
| Review… / Plan move… | action | ⚙ BE-N4 (modal m-dupe) · ⚙ BE-N3 |

### Analytics — screen 6 (15)
Space-by-type card: 1 title · 2 Assets · 3 Morphs · 4 Scenes · 5 Clothing bars [✅ `SpaceByTypeAsync`].
Top-by-creator card: 6 title · 7 Spacedog · 8 Kemenate · 9 Riccio bars [✅ `SpaceByCreatorAsync`].
Wasting-fast card: 10 title · 11 "190 GB" stat · 12 "review in Tiering →" [⚙BE-N2]. Usage-over-time card: 13
title · 14 "loads/day 90d" label · 15 sparkline [🔷 `IUsageAnalyzer` daily-loads series].

### Proposals & review — screen 7 (39)
Header: 1 "Reject all" · 2 "Approve selected" [⚙BE-N8]. Tabs: 3 All(7) · 4 Migrations(3) · 5 Duplicates(2) ·
6 Encoding(1) · 7 Stale(1) [⚙BE-N8 counts]. **Proposal card ×4** (rebalance / dedup / encoding-fix /
retire-stale) — each: 8/16/24/32 checkbox · 9/17/25/33 icon · 10/18/26/34 title · 11/19/27/35 detail line ·
12/20/28/36 tag (files/verified/high-conf/GB) · 13/21/29/37 Review… [→ m-migrate/m-dupe/m-fix/m-confirm] ·
14/22/30/38 Reject · 15/23/31/39 Approve [⚙BE-N8 — Approve dispatches ⚙BE-N3/N4/N5, Reject records].

### Health & fix — screen 8 (19)
Header: 1 "Fix all detected…" [⚙BE-N5 → m-fix]. Tabs: 2 Encoding(684) · 3 Integrity/corrupt(41) · 4
Missing-meta(12) [⚙BE-N5]. Group cards ×3: 5/8/11 label (GBK / Shift-JIS / low-confidence) · 6/9/12 number ·
7/10/13 Fix-group/Review btn [⚙BE-N5 GROUP BY codepage]. Detected table cols: 14 Package · 15 Detected ·
16 Broken n/m · 17 Confidence · 18 Fix/Review btn [⚙BE-N5 `EncodingFixCoordinator`] · 19 card wrapper.

### Health & fix (screen 8)
| Item | Kind | Backend |
|---|---|---|
| Tabs Encoding(684)/Integrity(41)/Missing-meta(12) | nav/data | ⚙ BE-N5 |
| Group cards GBK/GB18030, Shift-JIS, low-confidence (+counts) | data | ⚙ BE-N5 (GROUP BY codepage) |
| Detected table: package, detected, broken n/m, confidence, Fix/Review | data/action | ⚙ BE-N5 |
| "Fix all detected…" / per-row Fix (modal m-fix) | action | ⚙ BE-N5 (`EncodingFixCoordinator`) |
| Fix modal: before/after mojibake, "slim" checkbox, apply-to-group | action | ⚙ BE-N5 (+ slim = new option) |

### Missing dependencies — screen 9 (8)
Header: 1 "Export links txt" [⚙BE-N10]. Table cols: 2 Missing reference · 3 Needed-by count [✅
`IMissingDepsQuery`] · 4 Alias-to-owned · 5 Scope (global/preset) · 6 Edit-alias / 7 Resolve… btn [⚙BE-N11 →
m-alias] · 8 card wrapper.

### Trash & backup — screen 11 (12)
Tabs: 1 Trash (recoverable) · 2 Catalog backups [⚙BE-N6]. Toolbar: 3 count "2,140 items · 340 GB" · 4
Restore selected · 5 Purge selected [⚙BE-N6]. Trash table cols: 6 checkbox · 7 Package · 8 Reason · 9 Size ·
10 Trashed-when · 11 Restore btn [⚙BE-N6 `ITrashService.ListAsync`/`RestoreAsync`]. 12 Backups tab: list +
restore-backup [⚙BE-N6 `SqliteDatabaseBackup`].

### Activity history — screen 12 (5)
1 filter dropdown (All/Migrations/Deletes/Fixes) [🔷 `IActivityLog` kind filter]. Table cols: 2 When · 3
Action (tag) · 4 Package · 5 Detail [✅ `IActivityLog.GetRecentAsync`].

### Settings — screen 13 (11)
Tabs: 1 General · 2 Tiers & policy · 3 Automation · 4 Import · 5 Advanced [✅ `ISettingsService`]. Fields: 6
VaM install path [✅ `SettingsKeys.VamPath`] · 7 Catalog DB location [🔷 key] · 8 Symlink type [🔷 key] · 9
Fix-on-import (flag/prompt/auto) [✅ `SettingsKeys.FixOnImport`] · 10 Preset extraction defaults [🔷 keys] ·
11 Save btn [✅ `SetAsync`].

### Trash & backup (screen 11)
| Item | Kind | Backend |
|---|---|---|
| Tabs Trash / Catalog backups | nav | ⚙ BE-N6 |
| Trash table: checkbox, package, reason, size, trashed-when, Restore | data/action | ⚙ BE-N6 (`ITrashService.ListAsync/RestoreAsync`) |
| Restore selected / Purge selected | action | ⚙ BE-N6 |
| Backups list / restore backup | data/action | ⚙ BE-N6 (`SqliteDatabaseBackup`) |

### Activity history (screen 12)
| Item | Kind | Backend |
|---|---|---|
| Filter dropdown (All/Migrations/Deletes/Fixes) | filter | 🔷 `IActivityLog` (add kind filter) |
| Audit table: when, action(tag), package, detail | data | ✅ `IActivityLog.GetRecentAsync` |

### Settings (screen 13)
| Item | Kind | Backend |
|---|---|---|
| Tabs General/Tiers-policy/Automation/Import/Advanced | nav | ✅ `ISettingsService` |
| VaM install path, Catalog DB location | data/action | ✅ `SettingsKeys.VamPath` (+ db path) |
| Symlink type, Fix-on-import (flag/prompt/auto) | action | ✅ `SettingsKeys.FixOnImport` |
| Preset extraction defaults (morphs/hair/skin/…) | action | 🔷 settings keys (new) |
| Save | action | ✅ `SetAsync` |

### Modals & toast (91)
**m-onboard wizard (10)** [⚙BE-N13 + BE-N0]: 1–4 stepper (Add-drives/Benchmark/Index/Rescue-baseline) · 5
infobox · folder table cols 6 Folder · 7 Drive · 8 Benchmark [✅ `BenchmarkAsync`] · 9 Tier · 10 Back/Continue.
**m-addrepo (8)** [✅ Register/Benchmark + ⚙BE-N2]: 1 folder input · 2 Browse… · 3 okbox (detected
media/benchmark/tier/count) · 4 Tier select · 5 Reserve free space · 6 "Rebalance existing" chk [⚙BE-N2] · 7
Cancel · 8 Add & index [✅ `RegisterAsync` + BE-N0].
**m-migrate (14)** [⚙BE-N3]: 1 infobox · kv 2 Files-to-move · 3 Data · 4 Est-time · 5 Frees-on-T1 · flow 6
copy · 7 verify · 8 rename · 9 delete · table 10 Package · 11 From→To · 12 Size · 13 single-copy warnbox · 14
Cancel/Dry-run/Approve&run [✅ `MigrationRunner` copy→verify→rename→delete].
**m-fix (7)** [⚙BE-N5]: 1 detected line · 2 before (mojibake) · 3 after (UTF-8) · 4 okbox · 5 "slim" chk [🔷
new option] · 6 Cancel · 7 "Fix + apply to group (431)" [✅ `EncodingFixCoordinator`].
**m-alias (6)** [⚙BE-N11]: 1 missing-ref line · 2 map-to-owned search · 3 save-scope select (global/preset) ·
4 infobox · 5 Cancel · 6 Save alias.
**m-confirm delete (7)**: 1 single-copy warnbox [`DeletionPredicate`] · 2 reverse-dep check text [⚙BE-N9
reverse via `IDependencyGraph`] · table 3 Package · 4 Copies · 5 State (safe/protected) · 6 Cancel · 7 "Move
N safe items to trash" [⚙BE-N10 + `ITrashService`].
**m-preset (12)** [✅ `IPresetService` + ⚙BE-N10]: 1 name input · 2 Add-from-filter · 3 Import txt · 4 infobox
· member table 5 Package · 6 Mode · 7 Pin-version chk [🔷 `PresetMember` pin] · 8 remove × · 9 Export txt · 10
Cancel · 11 Save preset · 12 title.
**m-dupe (8)** [⚙BE-N4]: 1 header line · table 2 keep-radio · 3 Location · 4 Tier · 5 Integrity (verified) ·
6 okbox (full-hash verified, reclaim) · 7 Cancel · 8 "Keep T1, trash 3" [`DeletionPredicate` + trash].
**m-rescue (5)** [✅ `RescueAsync`]: 1 infobox · 2 currently-active stat · 3 baseline select (empty/preset) ·
4 Cancel · 5 Apply rescue baseline.
**m-vardetail (12)** [⚙BE-N9]: tabs 1 Overview · 2 Dependency graph [`IDependencyGraph` fwd+rev] · 3 Content
items [`ContentItem` + `IThumbnailStore`] · 4 Copies & lineage [`VarFile` + `FixedFromVarFileId`]; kv 5
Identity · 6 Fold key · 7 License · 8 Size · 9 Depended-on-by [`ReverseDependentCount`] · 10 Class · 11
infobox · 12 Close.
**Toast (2)**: 1 message · 2 Undo action [🔷 per-action undo — trash restore / activation revert].

### Coverage result

**464 items enumerated, 0 unmapped.** Every numbered item resolves to one of: ✅ bind an existing SDK
method · 🔷 a small extension of an existing SDK · ⚙ one of the new backend tasks **BE-N0…BE-N14** (all 15
are referenced) · or pure client-side UI (nav, theme, selection, filter chips). Roughly: **~40 %** of items
are backed today (✅/🔷 — Library read, Analytics, Activity, Repositories, Presets, Settings, Jobs), and
**~60 %** need one of the BE-N facades — overwhelmingly *facade + wiring over existing engines*, not new
algorithms. The per-screen counts sum exactly to the census total (20+20+3+40+78+42+16+23+22+15+39+19+8+12+5+11+91
= 464).

## Engine audit — algorithms are faithful, but they never run

Two separate questions: *do the engines implement the designed algorithms?* and *do they run in the
shipping app?* The answers diverge sharply.

### Fidelity: PASS (spot-checked the load-bearing algorithms)

The pure engines are careful, correct implementations of the sealed design — not fake logic:

| Engine | Designed algorithm | Verdict |
|---|---|---|
| `ContentSignatureEngine` | SHA-256 over **sorted multiset** of `(rawNameBytes, uncompressedSize, CRC-32)`; length-prefixed; dirs excluded; payload drops root `meta.json`; no-path variant; count-prefixed | ✅ faithful |
| `DeletionPredicate` | delete only if same `IdentityKey` copy is online **and** full `ContentHash` computed+equal; cross-identity never counts; heuristics never authorize | ✅ faithful |
| `DurableFileMover` | copy → WriteThrough+FlushFileBuffers → re-read & full-hash verify → atomic rename; source left for caller; `.partial` cleaned on failure/cancel | ✅ faithful |
| `IdentityFold` | NFC → `ToUpperInvariant` → re-NFC (handles upper-casing denormalization) | ✅ faithful |

### Reachability: FAIL — the pipeline has no production trigger

The application never runs the engines. Entry points:
- **CLI** ([Program.cs](../../src/VarVault.Cli/Program.cs)) prints the loaded module list and exits — no work.
- **GUI** ([AppHost.cs](../../src/VarVault.App/Composition/AppHost.cs)) resolves only `ILibraryQueryService` (a read) — it never indexes, resolves, dedupes, migrates, or fixes anything.

Consequences confirmed by grep:

0. **Nothing indexes.** `IIndexingService.IndexRepositoryAsync` has **45 callers — all in `tests/`**, zero
   in `src/`. In the shipping app the catalog is never populated, so the one Library screen is always
   empty and every downstream engine sits idle. There is no "add repository → index" flow, no background
   indexing, no trigger of any kind.
1. **These engines have no production caller** (only tests construct them) — because the flows/screens
   that would drive them don't exist: `DeletionPredicate`, `DedupGrouping`, `IntakeClassifier`,
   `MigrationPlanner`, `RebalancePlanner`. (`MigrationRunner`/`DurableFileMover` are reachable only from
   `MigrationRunner`, which itself has no production caller.)
2. **Dependency resolution never runs.** `IDependencyResolver.ResolveAllAsync` is called only by tests,
   and `EfCatalogStore.RefreshOneAsync` hard-sets `item.HasMissingDeps = false` (*"dependency resolution
   is Slice 2"*). The resolver-maintained bit is both never set (no caller) and would be clobbered on any
   read-model refresh. → Library "Missing deps" filter and the Missing-deps screen are empty at runtime.

**This is the real meaning of "the app is a stub":** the algorithms are built and E2E-tested against the
real 277-var corpus, but the only thing that invokes them is the test harness. The build plan's shell +
onboarding/add-repo/index flow (Slices 0–1) is what makes the engines *run*; the SDK facades (Slice 3)
are what let the action screens *drive* dedup/migration/reclaim/health.

### Fixes to fold into the slices

- **Slice 1 must include an index trigger**: an "Add repository → benchmark → index" flow (onboarding /
  repositories screen) calling `IRepositoryService` + `IIndexingService`, and post-index
  `IDependencyResolver.ResolveAllAsync`, so the Library is non-empty and missing-deps is populated.
- **Stop `RefreshOneAsync` clobbering `HasMissingDeps`** (recompute it, or don't reset it).
- **Slice 3 SDK facades** must expose delete/dedup/migration/reclaim so `DeletionPredicate` et al. gain a
  real caller — and add a runtime integration test that drives each from the SDK boundary (not just the
  engine unit).

*(Parallel audit agents are cross-checking orphaned registrations and test reliability; their findings
append here.)*

## Build plan (vertical slices) — each cites the BE tasks it needs

Order chosen so each slice ends with something runnable and testable (Avalonia.Headless), spine first.
BE-N ids refer to the *New backend tasks catalog* above.

**Slice 0 — Shell spine + make it run.** Rail nav (grouped + badges), `ShellViewModel` owning the active
screen + navigation command, top bar (search→palette, jobs, add-repo, theme), jobs panel (`IJobQueue`),
log dock. Real DI in `AppHost`: resolve the shell, register every screen VM. **BE-N0** (index
orchestration + trigger + `HasMissingDeps` fix). Headless test: navigation switches screen; an index job
populates the catalog; jobs panel reflects the queue.

**Slice 1 — Wire the backend-ready screens.** Views for **Analytics** (✅), **Activity** (✅ + 🔷 kind
filter), **Repositories** (✅ + BE-N13 add-repo), **Settings** (✅), **Missing deps** (✅, non-empty after
BE-N0). Each: `.axaml` + headless test asserting real data from its SDK service.

**Slice 2 — Complete the Library screen.** Rail views/tags/collections (**BE-N12**), facet chips (**BE-N12**
`LibraryQuery` extensions), per-type count + dep columns (🔷), ops/bulk bar (**BE-N10**), rich detail +
var-detail modal (**BE-N9**), alias resolve (**BE-N11**).

**Slice 3 — SDK facades + engine-only screens.** **Tiering & migration** (**BE-N2** + **BE-N3**),
**Duplicates & reclaim** (**BE-N4**), **Health & fix** (**BE-N5**), **Trash & backup** (**BE-N6**),
**Loading presets** switch (**BE-N7**), **Proposals** (**BE-N8**). Each facade ships with a *runtime*
integration test that drives the engine from the SDK boundary (closing the "never runs" gap).

**Slice 4 — Dashboard + modals + command palette.** **Dashboard** (**BE-N1**); dialog host + modal views
(onboarding **BE-N13**, add-repo, migrate **BE-N3**, fix **BE-N5**, alias **BE-N11**, confirm-delete
**BE-N10**, edit-preset, dupe **BE-N4**, rescue ✅, var-detail **BE-N9**); undo toast; Ctrl-K palette
(**BE-N14**).

**Slice 5 — Polish.** Theme tokens across all screens, keyboard nav/focus, empty/loading/error states
everywhere, smoke test that launches the shell and visits every screen headlessly.

## Definition of done for this plan

Every prototype screen has an `.axaml` view docked in the shell, bound to a real SDK service (no
placeholder VMs, no orphaned registrations), reachable by navigation, with a headless test proving it
renders real data — and the backend correctness gaps above are closed so the data is real, not empty.
