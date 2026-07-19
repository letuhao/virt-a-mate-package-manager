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
- [x] 0.4 Serilog: console + daily rolling file under data dir; structured startup line · M: `dotnet run VarVault.Cli` → `logs/VarVault-YYYYMMDD.log` contains "host composed with 2 modules"; `ILogger<T>` via `AddSerilog`
- [x] 0.5 xUnit tests run; central package management (Directory.Packages.props) · T: `dotnet test` → 23 passed, 0 failed
- *Foundation extras done:* SDK contract boundary (`IModule`/`IModuleContext`/`IPlugin`/`IEventBus`), `Result<T>`/`Guard`/`IClock` kernel, `PackageId` value object (·007 preserved, invalid rejected — tested), `global.json`, `.editorconfig`.

### Cross-cutting SDK (built — see [11](./11-Architecture-and-Modularity.md)/[12](./12-Engineering-Standards.md))
- [x] SDK-1 **Logging** — `ILogger<T>` via Serilog (`LoggingSetup`, console+file) · M: startup log line written
- [x] SDK-2 **Message bus** — `IEventBus.PublishAsync` → DI `IEventHandler<T>` + inline `Subscribe` · T: `HostCompositionTests` (handler invoked; inline sub receives/unsubscribes)
- [x] SDK-3 **Background jobs** — `IJobQueue` (Channel, bounded concurrency, `JobHandle` progress+cancel) · T: `ThreadingTests.JobQueue_runs_job_and_reports_progress`
- [x] SDK-4 **Single-writer queue** — `IWriteQueue` (priority, one consumer) · T: `ThreadingTests.WriteQueue_runs_writes_one_at_a_time` (maxConcurrent==1) + returns result
- [x] SDK-5 **Async mutual exclusion** — `AsyncLock` · T: `AsyncLockTests.Serializes_concurrent_sections`
- [x] SDK-6 **UI dispatcher** — `IUiDispatcher` contract + headless `InlineUiDispatcher` · T: resolves from host
- [x] SDK-7 **Progress** — `IProgressSink`/`ProgressReport` (+ `JobHandle` implements it)
- [x] SDK-8 **Persistence layer** — `VarVaultDbContext` (EF Core SQLite), `SqlitePragmas` (WAL/NORMAL/FK), `IUnitOfWork`/`EfUnitOfWork`, `AddVarVaultPersistence` · T: `PersistenceTests.Baseline_pragmas_enable_WAL_and_foreign_keys`
- [x] SDK-9 **Architecture enforcement** — `VarVault.Architecture.Tests` (NetArchTest) · T: 4 tests pass
- [x] SDK-10 **Standards** — [CLAUDE.md](../../CLAUDE.md), [12-Engineering-Standards](./12-Engineering-Standards.md), [13-UI-UX-Standards](./13-UI-UX-Standards.md)

### Testing & observability (built — see [14](./14-Testing-and-Observability-Standards.md))
- [x] TO-1 **TestKit harness** — `TestHost` (composed host + FakeClock + captured logs + optional SQLite), `SqliteTestDatabase`, `TempDirectory`, `CapturingLoggerProvider`, `PackageIds`, `TestCategories` · used by Host/Infra/E2E tests
- [x] TO-2 **Host test seam** — `VarVaultHost.Build(options, configure, modules)` service-override hook · T: TestKit overrides `IClock`
- [x] TO-3 **Test categories** — `[Trait("Category", …)]`; filterable · M: `dotnet test --filter "Category=E2E"` runs only E2E
- [x] TO-4 **Integration tests** — real SQLite via fixture · T: `DatabaseIntegrationTests`, `PersistenceTests` (WAL/FK)
- [x] TO-5 **E2E harness** — full flow (event→handler→job→metric→write→health→logs) · T: `FoundationFlowTests` passes
- [x] TO-6 **Metrics** — `Telemetry` Meter/instruments (writes/jobs counters+durations, depth/active gauges); write & job queues instrumented · T: E2E asserts `jobs.completed` via `MetricCollector<long>`
- [x] TO-7 **Tracing** — `ActivitySource` "VarVault"; write/job activities
- [x] TO-8 **Health checks** — `write-queue` (liveness) + `database` (readiness) via `HealthCheckService` · T: E2E asserts `Healthy`
- [x] TO-9 **Log-as-probe** — `CapturingLoggerProvider` asserts structured entries · T: E2E asserts startup line
- [x] TO-10 **UI-E2E** — Avalonia.Headless.XUnit harness · T: `MainWindowUiTests.Main_window_shows_library_rows_after_refresh` (`[AvaloniaFact]` under the headless dispatcher: window binds `MainWindowViewModel`, ListBox renders 4 rows after refresh) — `TestAppBuilder` (`[AvaloniaTestApplication]`)
- *Total suite: 25 tests across 5 test projects, all green.*

### Database & migrations
- [x] 0.6 SQLite via EF Core 10; connection opens `WAL` + `synchronous` set per policy · T: `CatalogSchemaTests.Connection_uses_wal_journal_mode` + `PersistenceTests.Baseline_pragmas_enable_WAL_and_foreign_keys`
- [x] 0.7 First EF migration creates the schema; migrate-up on empty DB succeeds · T: `CatalogSchemaTests.Migration_creates_all_expected_tables` (25 tables incl. FTS); `dotnet ef database update` applies `20260718215021_InitialCatalogSchema` clean
- [x] 0.8 `PRAGMA foreign_keys=ON` enforced at connection open · T: `CatalogSchemaTests.Foreign_key_violation_is_rejected` (VarFile→nonexistent Repository throws `DbUpdateException`)
- [x] 0.9 Startup `PRAGMA integrity_check` runs; corrupt DB is detected and surfaced · T: `CatalogSafetyTests.Integrity_check_detects_a_corrupt_database` (corrupted SQLite header → failure Result) + `Integrity_check_passes_on_healthy_database` + `Initializer_prepares_a_fresh_database` (`CatalogDatabaseInitializer`: guard→migrate→pragmas→integrity)
- [x] 0.10 ⚠ DB refuses to open if located on a repo/removable volume · T: `CatalogSafetyTests.Rejects_db_on_non_fixed_volume` (Removable/Network/CDRom) + `Rejects_db_nested_within_a_repository` + `Rejects_repository_nested_within_the_db_directory` (`CatalogLocationGuard`)
- [x] 0.11 App-computed Unicode fold-key function: NFC + full case-fold; ASCII and CJK both covered · T: `IdentityFoldTests` (ASCII Café==café, Cyrillic МЕ==ме, CJK preserved, NFC composed==decomposed, idempotent) — see `IdentityFold`

### Schema — entities & constraints (each = table created + constraints enforced)
- [x] 0.12 `Repository` table + `VolumeSerial`, tier, priority, capacity fields · T: `CatalogSchemaTests.Repository_round_trips_all_fields`
- [x] 0.13 `Package` — unique `IdentityKey`, `(Creator,PackageName,VersionSort)` composite (not unique alone), unique `VarName`; `CanonicalVarFileId` nullable ON DELETE SET NULL · 🔒 T: `Duplicate_identity_key_is_rejected` + `Deleting_canonical_varfile_nulls_pointer_not_package` (Package survives, pointer nulled)
- [x] 0.14 `VarFile` — unique `(RepositoryId,RelativePath)`; FK PackageId nullable · T: `Duplicate_repo_relativepath_is_rejected` (migration `PackageId nullable:true`, SetNull)
- [x] 0.15 `ContentItem` keyed on `VarFileId` (🔒 not PackageId) · T: `ContentItem_cascades_with_its_varfile`
- [x] 0.16 `Dependency` — unique `(VarFileId,DependsOnRefKey)` · T: `Duplicate_dependency_edge_is_rejected`
- [x] 0.17 `UserSave` + `SaveDependency` tables exist · T: `Migration_creates_all_expected_tables` + `Remaining_entity_groups_round_trip`
- [x] 0.18 `UsageEvent` (UTC epoch) + `UsageStat` tables · T: `UsageEvent_timestamp_is_stored_as_integer_epoch` (`typeof`==integer, value round-trips)
- [x] 0.19 `MigrationJob` — unique partial index `(VarFileId) WHERE State NOT IN (Done,Failed,Cancelled)` · ⚠ T: `Only_one_live_migration_job_per_file` + `Terminal_migration_job_does_not_block_a_new_live_job`
- [x] 0.20 `Profile`, `ActivationLink`, `LoadingPreset`, `PresetMember`, `VarAlias` tables · T: `Migration_creates_all_expected_tables` + `Remaining_entity_groups_round_trip`
- [x] 0.21 `Tag`/`PackageTag`/`Collection`/`CollectionMember`/`ContentItemPref` tables · T: `Migration_creates_all_expected_tables` + `Remaining_entity_groups_round_trip`
- [x] 0.22 `PackageListItem` materialized table + `TrashItem` + `Setting` · T: `Migration_creates_all_expected_tables` + `Remaining_entity_groups_round_trip`
- [x] 0.23 FTS5 `PackageSearch` virtual table with the chosen **CJK-capable tokenizer** (trigram, D1) · 🔒 T: `Fts5_trigram_matches_spaceless_cjk` (MATCH on space-less CJK returns the row)
- [x] 0.24 Indexes from data-arch §6 created; `EXPLAIN QUERY PLAN` uses them · T: `SchemaIndexTests` (identity unique, (repo,path) unique, ContentSignature, gallery composite with no temp B-tree). *`ANALYZE` runs post-bulk during indexing (1.25).*

### Recompute pipeline (single writer)
- [ ] 0.25 Single-writer queue: all writes serialize through one connection; interactive writes prioritized over bulk (T: concurrent favorite-toggle completes while bulk batch runs)
- [ ] 0.26 Dirty-set mechanism: a base-table write enqueues affected derived rows (T: insert VarFile → its Package's PackageListItem row refresh scheduled)

---

## Slice 1 — Repositories, indexing, library browse

### Repository registration & tiering
- [x] 1.1 Register a repo by folder path; row persisted · T: `RepositoryRegistrationFlowTests.Registers_profiles_and_lists_a_repository` (register → RepositoryInfo → appears in `ListAsync`) — `RepositoryService.RegisterAsync`
- [x] 1.2 🔒 Reject repo path equal to or nested within `{vampath}\AddonPackages`, or overlapping another repo · T: `RepositoryPathValidatorTests` (AddonPackages + subfolder, same/nested/parent overlap) + `RepositoryRegistrationFlowTests.Rejects_a_repository_overlapping_an_existing_one` — `RepositoryPathValidator`
- [x] 1.3 Detect drive media type (NVMe/SSD/HDD/Network/Removable) · T: `DriveProfilerTests.Profiles_a_fixed_volume...` (fixed drive → NVMe/SSD/HDD, never Removable/Network) — `DriveProfiler` (seek-penalty + bus-type Win32 queries, HDD fallback)
- [ ] 1.4 Capture `VolumeSerial` on register (done); on re-point, mismatch → read-only + prompt (⚠ pending) · T: `DriveProfilerTests.Volume_serial_is_read_on_windows` (capture) — re-point strict-match guard still to build
- [ ] 1.5 Benchmark read/write MB/s on register (B: numbers within ~20% of a known reference tool)
- [ ] 1.6 Auto-assign tier from benchmark; manual override persists (M) — *tier currently from media type (BE-R5); benchmark re-tier pending*
- [x] 1.7 Live free/total capacity; refresh on demand · T: `DriveProfilerTests.Capacity_refresh_matches_drive_info` + `RepositoryService.RefreshCapacityAsync` (matches `DriveInfo`)
- [ ] 1.8 `MinFreeBytes` reserve stored and honored by placement (T) — *field stored; placement engine pending*
- [x] 1.9 Enable/disable repo; disabled state persists · T: `RepositoryRegistrationFlowTests.Enable_disable_persists` — `RepositoryService.SetEnabledAsync` (*scan-exclusion of disabled repos wires in when the multi-repo scan driver lands*)
- [ ] 1.10 Offline detection: unplugged repo → `IsOnline=false`, its VarFiles marked unavailable, **not pruned** (⚠ T: offline repo's rows survive a scan)

### Indexing pipeline (see [06](./06-Feature-Specs-Indexing.md))
- [x] 1.11 Enumerate `*.var` recursively; exclude link-farm dirs and reparse points; quarantine tagged/omittable · T: `RepositoryEnumeratorTests` (`Finds_live_vars_excludes_link_dirs_tags_quarantine`, `Skips_reparse_point_directories`, `Include_quarantined_false_omits_quarantined_vars`) + real-repo probe — `RepositoryEnumerator` (manual walk, skips `ReparsePoint`, `RepositoryScanRules.IsLinkDirectory`)
- [x] 1.12 Freshness skip by `(size, mtime)` from directory entry **without opening the file** · T: `RepositoryScanRulesTests` (`IsFresh` size/mtime + 2 s tolerance window) + `RepositoryEnumeratorTests.Captures_size_and_mtime_without_opening_files` (enumerator only stats, never opens)
- [x] 1.13 🔒 Identity parse: 3-part `Creator.Package.Version`, digit version; else failure → Unrecognized bucket · T: `IdentityFacetsTests` (`.007` preserved, `A.B.C.1`/non-numeric → failure, `Long_version_clamps_without_overflow` 20-digit → `long.MaxValue`)
- [x] 1.14 🔒 `IdentityKey` computed; `Creator.Pkg.007` and `creator.pkg.007` collapse to same key · T: `IdentityFoldTests` + `IdentityFacetsTests.Dot_seven_and_dot_zerozeroseven_stay_distinct_versions` (case collapses, `.7`≠`.007`)
- [x] 1.15 Facets (Creator/PackageName/VersionToken/VersionSort) parsed **from filename** · T: `IdentityFacetsTests` — `PackageId.TryParse` derives every facet from the filename and structurally cannot read meta.json (identity is filename-only by construction; `MetaDivergent` set separately at index time, 1.17)
- [x] 1.16 Read `meta.json`; missing → `IntegrityStatus=MissingMeta` · T: `VarInspectorTests` (`Inspects_a_healthy_var_end_to_end` reads meta, `Missing_meta_is_flagged`) + `VarMetaParserTests` (tolerant parse, CJK dep ref) — `VarInspector` + `VarMetaParser`
- [x] 1.17 Store `MetaCreator`/`MetaPackage` + `MetaDivergent` flag when they differ from filename · T: `IndexingFlowTests.Meta_divergent_creator_is_flagged` (filename Creator ≠ meta creatorName → `MetaDivergent`=true, `MetaCreator` stored) — `VarUpsert.MetaDivergent` (folded compare)
- [x] 1.18 Content classification by prefix+ext rules → per-type counts; matches legacy type table · T: `ContentClassificationEngineTests` (scene/look/clothing/hair/asset counts, preset flags per legacy table, plugin cslist-else-cs, case/separator-agnostic) + `RealRepoClassificationTests` (real corpus classifies)
- [x] 1.19 `PrimaryType` chosen by fixed precedence · T: `ContentClassificationEngineTests` (scene wins over look/clothing; `ChoosePrimary` precedence) + `RealRepoClassificationTests` (every classified real var → non-Unknown primary)
- [x] 1.20 🔒 `ContentSignature` over sorted `(rawEntryNameBytes, uncompressedSize, CRC-32)` multiset; **raw bytes**, Zip64-aware, dir-entries excluded · T: `ZipCentralDirectoryReaderTests.Same_content_different_compression_and_order_yields_same_content_signature` (byte-different zips → identical signature) + `ContentSignatureEngineTests.Raw_bytes_drive_identity_so_mojibake_is_deterministic`
- [x] 1.21 `PayloadSignature` (excludes meta.json) computed · T: `ContentSignatureEngineTests.Payload_signature_excludes_meta_json` + `ZipCentralDirectoryReaderTests.Different_meta_only_shares_payload_signature_not_content`
- [x] 1.22 `ContentSignatureNoPath` (size+CRC, paths excluded) computed · T: `ContentSignatureEngineTests.NoPath_signature_ignores_names_but_not_sizes`
- [ ] 1.23 Staged: pass-1 (names+meta+deps) makes catalog browsable before pass-2 (previews+signatures) finishes (M/D: browse during index of a real repo)
- [ ] 1.24 Per-physical-drive parallelism: HDD degree=1, NVMe higher (M: two repos on one HDD don't run concurrent reads)
- [ ] 1.25 Bulk writes batched in transactions; FTS triggers disabled during bulk then rebuilt once (T/B: bulk insert rate acceptable)
- [x] 1.26 Incremental re-index: second scan of unchanged repo opens 0 files · T: `IndexingFlowTests.Reindex_is_incremental_and_prunes_vanished_files` (2nd scan: Skipped=2, Indexed=0 → no var opened, freshness by size/mtime only)
- [x] 1.27 Corrupt zip → `IntegrityStatus=CorruptZip`, not indexed as content · T: `VarInspectorTests.Corrupt_zip_is_flagged_without_throwing` (empty entries, no signatures) + `ZipCentralDirectoryReaderTests.Corrupt_zip_is_rejected`
- [x] 1.28 Prune: VarFile whose file vanished (repo confirmed online) removed; offline → kept unavailable · ⚠ T: `IndexingFlowTests.Reindex_is_incremental_and_prunes_vanished_files` (online: Pruned=1, row gone) + `Offline_repository_does_not_prune_vanished_files` (offline: Pruned=0, row survives)
- [x] 1.29 Quarantine-dir recognition (`___VarRedundant___` etc.) → `QuarantineKind` set, not treated as live · T: `RepositoryScanRulesTests.Classifies_quarantine_directories_by_prefix` (incl. legacy suffix) + `RepositoryEnumeratorTests` (tagged Redundant, `IsLive`=false) + real-repo probe
- [ ] 1.30 Filesystem watch triggers incremental re-index of changed files (M: drop a var → appears)
- [x] 1.31 D: full index of a real var repo completes; reports counts + flagged files · T/D: `IndexingFlowTests.Indexes_the_real_repository_corpus` (indexes `D:\VarVault_test_repo` — 277 vars incl. quarantine dirs — into SQLite in ~9 s; asserts packages/varfiles/read-model rows created, quarantined tagged). *Note: current writer does per-var SaveChanges; transaction batching (1.25) is the pending throughput optimization.*

### Preview extraction & thumbnails
- [ ] 1.32 Sibling `.jpg` extracted for previewable content items (T)
- [ ] 1.33 Thumbnails in a **packed store** (not 700k loose files) keyed by PackageId on fastest tier (M: store is N blob files)
- [ ] 1.34 Representative preview per package by PrimaryType; preview-less types get a placeholder (M)
- [ ] 1.35 Thumbnails decode off UI thread with scroll-ahead prefetch (M: no UI stall on fast scroll)

### Read model & search
- [x] 1.36 `PackageListItem` populated/refreshed after indexing · T: `IndexingFlowTests.Indexes_a_repository_into_the_catalog` (row appears with `PrimaryType`=Scene, aggregates) — `EfCatalogStore.RefreshReadModelAsync`
- [x] 1.37 `OnlineInstanceCount` vs `TotalInstanceCount` distinct; `IsSingleCopy` derived from online count · ⚠ T: `IndexingFlowTests` (OnlineInstanceCount=1, IsSingleCopy=true) + `Offline_repository_does_not_prune...` (online count reflects repo online state)
- [x] 1.38 Composite index per sort order; sort query uses index (no temp B-tree) · T: `SchemaIndexTests.Gallery_class_sort_uses_composite_index_without_temp_btree` (EXPLAIN QUERY PLAN) + `LibraryQueryFlowTests` (filter/sort/page over the read model) — `ILibraryQueryService`/`EfLibraryQueryService` maps each `LibrarySort` to a §6 composite index
- [ ] 1.39 `OrderedSnapshot` built per (filter,sort); random `rows[i]` is O(1) (B: matches spike ~0.08 ms)
- [x] 1.40 FTS search returns results incl. CJK · T: `LibraryQueryFlowTests.Fts_search_finds_cjk_creator_names` (index a 刘亦菲 var → search "刘亦菲" hits exactly it) — `EfCatalogStore.UpdateSearchIndexAsync` populates the trigram `PackageSearch` blob; `EfLibraryQueryService.SearchIdsAsync` runs `MATCH`
- [ ] 1.41 Faceted count debounced + approximate during typing, exact on settle (M)
- [ ] 1.42 B: read-path perf on real 70k catalog matches the [spike](./05-Perf-Spike-Results.md) envelope (paging <5 ms, scrollbar jump <1 ms)

### Library UI (Slice-1 screens)
- [ ] 1.43 Table view: content-count columns, tier, state, virtualized scroll (no pager) (S)
- [ ] 1.44 Click-header sort with direction indicator; multi-sort (M)
- [ ] 1.45 Gallery view: virtualized thumbnail wall; toggle with table (S)
- [ ] 1.46 Searchable creator combobox: type-filter + keyboard nav on real creator list (M)
- [x] 1.47 Filters apply (creator / search / favorites / missing-deps); sort state flows to the query · T: `LibraryViewModelTests.Filter_and_sort_state_flow_into_the_query` + `LibraryQueryFlowTests` (filtered results) — `LibraryViewModel` (*Installed/Reset filters + visual grid land with the Avalonia view*)
- [ ] 1.48 Detail panel: metadata, content-preview strip (type filter/loadable/hide-fav), copies, dependencies (S)
- [ ] 1.49 Select-all-matching vs select-visible; persistent selection across scroll (M)
- [ ] 1.50 Empty / loading / partial-index / offline-repo-row / error states each rendered (S each)
- [ ] 1.51 Grid keyboard nav (arrows/space/shift-select) (M)
- [ ] 1.52 Remembered view: filters/sort/columns persist across restart (M)
- [ ] 1.53 Jobs tray shows the live index job with progress + cancel (M: cancel stops it)

---

## Slice 2 — Dependency engine & missing

- [x] 2.1 Harvest dependency edges from `meta.json` AND embedded scene/`.vap` JSON (tolerant scan) · T: `DependencyResolutionFlowTests.Embedded_scene_refs_are_harvested_as_dependencies` (a scene ref absent from meta.json → `Dependency` with `RefKind.Embedded`, resolves) — `VarInspector` (one archive pass) + `EmbeddedRefExtractor`
- [x] 2.2 🔒 `SELF` refs resolve to container package, excluded from missing · T: `DependencyRefTests.Self_ref_is_flagged_when_family_matches_container` + `EfDependencyResolver.ResolveOne` (SELF → container, `RefKind.Self`, not missing)
- [x] 2.3 Unparseable refs recorded (flag), never silently dropped · T: `DependencyRefTests.Rejects_unparseable_refs`; resolver marks them `IsMissing`/`ResolvedVia.None` (row retained)
- [x] 2.4 `UNIQUE(VarFileId,DependsOnRefKey)`; self-edges excluded from centrality · T: `CatalogSchemaTests.Duplicate_dependency_edge_is_rejected` + resolver skips `target==source` in reverse counts
- [x] 2.5 🔒 Version resolution: exact / `latest`=highest / closest-newer-else-newest-older; `$` substitution flagged · T: `VersionResolverTests` (5 cases) + `DependencyRefTests.Substitution_token_is_flagged_and_treated_as_latest`
- [ ] 2.6 Per-`(Creator,Package)` "current latest" pointer; `latest` deref O(1) (T) — *resolver builds an in-memory family map (latest via MaxBy); a persisted O(1) pointer is a later optimization*
- [ ] 2.7 Incremental re-resolve on new version: only edges targeting that `(Creator,Package)` touched; audit event emitted (T) — *currently a full-catalog pass; incremental scoping pending*
- [x] 2.8 `IsMissing`/`ResolvedPackageId` written in one pass **including alias application**; `ResolvedVia` set · T: `DependencyResolutionFlowTests.Resolves_present_deps_and_flags_missing` + `Closest_version_substitution_is_recorded` — `EfDependencyResolver.ResolveAllAsync`
- [x] 2.9 🔒 Alias precedence: present real match outranks alias · T: `DependencyResolutionFlowTests.Alias_resolves_a_missing_ref_but_a_real_match_outranks_it` (alias only used when no real match; real match → `Exact`)
- [x] 2.10 Forward closure via recursive CTE with cycle detection + depth cap · T: `ForwardClosureFlowTests` (`Chain_resolves_transitively` A→B→C → {B,C}; `Cycle_terminates` A→B→A → {B}, no infinite loop) — `EfDependencyGraph.ForwardClosureAsync` (`UNION` set semantics + depth cap)
- [x] 2.11 ⚠ Reverse closure: `ReverseDependentCount` + `IsFoundational` precomputed; NO on-demand enumeration · T: `DependencyResolutionFlowTests.Foundational_flag_set_when_many_packages_depend_on_one` (in-degree counted, `IsFoundational` at threshold) + `Resolves_present_deps...` (count=1) — precomputed on `Package`, never enumerated interactively
- [x] 2.12 Reference check for safe-delete spans Dependency + SaveDependency + PresetMember + VarAlias + ActivationLink · ⚠ T: `UserSaveFlowTests.Var_needed_only_by_a_user_save_is_not_an_orphan` (a var referenced only by a UserSave → `IsReferenced`=true; a truly unreferenced var → false) — `IReferenceQuery`/`EfReferenceQuery`
- [x] 2.13 UserSave scan of loose scenes/presets → SaveDependency edges · T: `UserSaveFlowTests` (scans a scene JSON, records UserSave + resolved SaveDependency) + `EmbeddedRefExtractorTests` (packaged-ref extraction incl. CJK) — `UserSaveScanner` + `EmbeddedRefExtractor`
- [ ] 2.14 Missing-deps screen lists refs with needed-by counts; scan variants (installed/all/filtered/saves) (M)
- [x] 2.15 `HasMissingDeps` materialized bit on read model, direct-only · T: `DependencyResolutionFlowTests.Resolves_present_deps_and_flags_missing` (consumer with a missing dep → `HasMissingDeps`=true; base package → false) — `EfDependencyResolver.UpdateHasMissingDepsAsync` (canonical var's direct deps)

---

## Slice 3 — Activation, presets, aliases

- [ ] 3.1 🔒 Profile = directory under `___AddonPacksSwitch ___`; `AddonPackages` is a directory symlink to active profile (M: filesystem reflects it)
- [ ] 3.2 🔒 Switch preset = repoint the one directory symlink; O(1) regardless of var count (B: switch time flat for 100 vs 5000 vars)
- [x] 3.3 Symlink creation works under Developer Mode; clear error if unavailable · T: `SymlinkServiceTests.Repoint_refuses_to_replace_a_real_directory` (never clobbers a real dir) + privilege-not-held (Win32 1314) → actionable `symlink.privilege` error with a Developer-Mode hint — `SymlinkService` (*positive create/repoint path verified on a Dev-Mode machine; skips in the privilege-less sandbox*)
- [ ] 3.4 ActivationLink keyed by `VarFileId`; picks hottest **online** copy (⚠ T)
- [ ] 3.5 `LinkKind` (Install/Alias/Temp) + `AliasedMissingRef` + `Reason` (Explicit/DependencyOf/Temp) recorded (T)
- [x] 3.6 Loading preset CRUD; members by name; ResolvedPackage + IsVersionSubstituted · T: `PresetFlowTests.Duplicate_preset_name_is_rejected_and_delete_works` + create/add-member resolving members by folded name — `IPresetService`/`EfPresetService` (*pin flag + ResolvedVersion snapshot column exist on `PresetMember`; UI pin toggle lands with the preset screen*)
- [ ] 3.7 Activate preset = resolve members → forward closure → apply aliases → create links; missing reported (M/D)
- [x] 3.8 Dependency-aware activation pulls closure; preview "will pull in N" before apply · T: `PresetFlowTests.Create_preset_and_preview_pulls_in_the_dependency_closure` (preset member A.Look.1 → DirectResolved=1, TotalWithClosure=2 incl. its Base dep; missing ref reported) — `EfPresetService.PreviewActivationAsync`
- [ ] 3.9 🔒 Persistent aliases (global + per-preset) re-apply automatically on every load — no re-setup (T: switch away and back, alias still applied)
- [x] 3.10 Alias/preset serialized by **var-name string** (portable); import re-resolves with reported diff · T: `PresetImporterTests.Reports_found_substituted_and_unknown` (export→import against a different library shows found/substituted/unknown) — `PresetImporter.Analyze`
- [x] 3.11 Import/export preset from txt; validate on import, flag unknowns/version mismatches · T: `PresetTextFormatTests` (round-trip, comments/blanks) + `PresetImporterTests` (version-mismatch→substituted, unknown, unparseable flagged) — `PresetTextFormat` + `PresetImporter`
- [ ] 3.12 Reconcile: links the app created are owned/marked; reconcile never deletes user-made links or real files (⚠ T)
- [ ] 3.13 Rescue baseline: deactivate all → minimal set; game launches (M/D)
- [ ] 3.14 Temp activation auto-cleaned after use (T)
- [ ] 3.15 Deactivation reference-counts DependencyOf links; drops only unneeded ones (T)

---

## Slice 4 — Duplicates, reclaim, encoding fix

### Deletion predicate (⚠ gates ALL delete paths)
- [x] 4.1 🔒⚠ A VarFile is deletable ONLY if another copy of the **same IdentityKey** is online AND full `ContentHash` verified equal AND it's not the last online copy · T: `DeletionPredicateTests` (each precondition blocks: `Blocks_when_candidate_hash_not_computed`, `Blocks_single_online_copy`, `Blocks_when_online_duplicate_hash_differs`, `Allows_delete_when_a_verified_online_duplicate_exists`) — `DeletionPredicate.Evaluate`
- [x] 4.2 🔒⚠ Cross-identity ContentSignature matches are report-only, never delete candidates · T: `DeletionPredicateTests.Cross_identity_copy_is_never_a_safety_net` + `DedupGroupingTests.Same_content_across_identities_is_report_only`
- [x] 4.3 ⚠ Single-copy hard gate excludes them from all bulk/auto delete paths · T: `DeletionPredicateTests.Blocks_single_online_copy` (offline sibling is not a safety net → the sole online copy is undeletable)
- [x] 4.4 ⚠ Group with any offline member is blocked from dedup deletion · T: `DedupGroupingTests.Group_with_offline_member_is_flagged_unsafe` (`DedupGroup.AllOnline`=false)

### Duplicates & reclaim
- [x] 4.5 Dedup grouping by ContentSignature within one identity · T: `DedupGroupingTests` (within-identity dup groups, content-conflict same-identity-diff-sig excluded, cross-identity report-only) + `RealRepoDedupTests` (groups the real corpus by structural signature) — `DedupGrouping.Analyze`
- [x] 4.6 Near-dup by PayloadSignature (same content, diff meta) flagged separately · T: `IntakeClassifierTests.Near_duplicate_different_identity_same_payload` — `IntakeClassifier` (payload match across identities → NearDuplicate)
- [x] 4.7 Download-intake classification: exact / same-name-different / near-dup / encoding-variant / new · T: `IntakeClassifierTests` (all five classes + exact-wins precedence) — `IntakeClassifier.Classify`
- [ ] 4.8 Reclaim wizard aggregates duplicates + cold-on-SSD + never-loaded orphans (single-copy excluded) with size estimates (M)
- [ ] 4.9 Duplicate review: keep-one, ranked by integrity/health; verified-before-delete (M)

### Encoding health & fix (see [06](./06-Feature-Specs-Indexing.md))
- [x] 4.10 Per-entry codepage detection (GBK/GB18030/Shift-JIS/Big5/EUC-KR) with round-trip validation; `DetectedCodepage`+`BrokenEntryCount` computed · T: `EncodingHealthEngineTests` (GBK detected; UTF-8 flag & valid-UTF-8-without-flag & ASCII all Ok = 0 false-positive; undetectable→NeedsFix broken-count; mixed→PartiallyBroken) + `RealRepoClassificationTests.Encoding_detection_runs_over_the_real_corpus...` — `EncodingHealthEngine.Detect` (storage on `VarFile` at index time, IDX-8)
- [x] 4.11 CodePagesEncodingProvider registered; raw entry-name bytes captured · T: `EncodingHealthEngineTests` (engine registers `CodePagesEncodingProvider`; `ZipCentralDirectoryReader` captures raw name bytes + UTF-8 flag bit 11)
- [x] 4.12 🔒⚠ Fix writes a NEW UTF-8 var (never overwrite in place); temp→validate→atomic rename · T: `EncodingFixerTests.Fixes_a_gbk_broken_var_into_valid_utf8` (`.partial`→validate→`File.Move`) + `Refuses_to_overwrite_an_existing_output` + `No_partial_file_remains_after_success` — `EncodingFixer`
- [x] 4.13 🔒 Fixed var validated against VaM constraints (Deflate, UTF-8 flag, meta present) before preferring it · T: `EncodingFixerTests` (fixed var → `EncodingHealth.Ok` + `VamVarValidator.Validate` success; 衣装 decodes correctly, non-ASCII names flagged UTF-8) — `VamVarValidator`
- [ ] 4.14 ⚠ Original retained (not trashed) until fix confirmed (done); `FixedFromVarFileId` lineage set (pending) · T: `EncodingFixerTests.Fixes_a_gbk_broken_var_into_valid_utf8` (original untouched, still `NeedsFix`) — DB lineage wiring pending
- [ ] 4.15 ⚠ Auto/batch mode flags low-confidence for review, never deletes originals unattended (T)
- [ ] 4.16 Health report grouped by codepage; batch "fix all" (M)
- [ ] 4.17 Optional slimming is separate, off by default (T)

---

## Slice 5 — Analyzer, placement, migration

- [x] 5.1 UsageEvent appended on every app-performed activate/load; recompute updates class · T: `UsageAnalyzerFlowTests.Records_events_and_recomputes_classification` (12 loads → UsageStat Use30d=12, Class=Hot, read-model class updated) — `EfUsageAnalyzer`
- [x] 5.2 UsageStat windowed counts computed time-relative (correct the day after) · T: `WindowedUsageTests` (`Windows_are_time_relative_as_the_clock_advances` — same event drops out of the 30d window 10 days later with no new events) — `WindowedUsage.Compute`
- [x] 5.3 UsageEvent compaction of >90d into rollups · T: `UsageCompactionTests.Compacts_old_events_into_a_rollup_preserving_total` (3 old events → RolledUpUseCount=3, deleted; UseCountTotal=5 preserved) — `EfUsageAnalyzer.CompactAsync` (transactional)
- [x] 5.4 Hot/warm/cold scoring blends recency+frequency+centrality+overrides with hysteresis; `Class` reproducible from stored state · T: `UsageScoringTests` (hot/cold cases, pin/force overrides, 7-day cooldown blocks flip, deadband stability, reproducible from same inputs) — `UsageScoring.Score` (BE-A3/A4/A5/A8)
- [x] 5.5 Placement policy maps Class→tier; misplaced set computed · T: `PlacementPolicyTests` (Hot→1/Warm→2/Cold→3; misplaced when actual≠desired) — `PlacementPolicy` (BE-P1)
- [x] 5.6 Migration planner diffs actual vs target → proposals; single-copy & offline & unsafe-target excluded · ⚠ T: `MigrationPlannerTests` (hot-on-cold → propose to tier 1; correctly-placed → none; excludes offline/single-copy/unsafe-target) — `MigrationPlanner.Plan` (propose-only)

### Migration durability (⚠ every item data-loss-critical)
- [x] 5.7 🔒⚠ State machine Planned→Copying→Verifying→Renaming→Deleting→Done, idempotent resume · T: `MigrationFlowTests.Migrates_a_var_between_repos_and_trashes_the_source` (Planned→Done) + `Resume_from_copying_completes_the_migration` (a job persisted in Copying resumes to Done) — `MigrationRunner` (target row inserted post-verify; skips already-done steps)
- [x] 5.8 🔒⚠ Copy to `.partial` temp → flush (WriteThrough/`FlushFileBuffers`) → verify by full-hash re-read → atomic rename; source left for the caller to delete last · T: `DurableFileMoverTests` (verified copy, source preserved, no leftover `.partial`, refuses overwrite, cancellation leaves nothing) — `DurableFileMover` (*cache-bypassed `FILE_FLAG_NO_BUFFERING` verify is a later hardening*)
- [ ] 5.9 🔒⚠ `synchronous=FULL` on any transaction gating a destructive FS op (T)
- [x] 5.10 ⚠ Free-space reservation ledger; concurrent jobs can't overfill past MinFree · T: `FreeSpaceLedgerTests` (reserve within free−minFree, concurrent reservations can't breach MinFree, release frees capacity, per-repo budgets) — `FreeSpaceLedger`
- [x] 5.11 ⚠ Never migrate TO removable/network tier · T: `MigrationFlowTests.Refuses_to_migrate_to_a_removable_target` (Removable target → job Failed, source untouched) — `MigrationRunner`
- [x] 5.12 ⚠ Re-point ActivationLink/CanonicalVarFileId/refs to surviving copy BEFORE deleting source · T: `MigrationFlowTests.Migrates_a_var_between_repos...` (`CanonicalVarFileId` re-points to the target row; ActivationLinks re-pointed) — `MigrationRunner` re-points before trashing the source (which goes to trash, X.1)
- [x] 5.13 Interrupted copy leaves temp only; destination appears only post-verify · T: `DurableFileMoverTests.Cancellation_leaves_no_destination` + `Copies_verifies_and_renames...` (dest created only after verify via atomic rename) — `DurableFileMover` (*indexing already skips `.partial` since it enumerates `*.var`; target DB row post-verify wires in with the migration orchestrator*)
- [ ] 5.14 Proposals inbox: all pending migrations/dedup/fixes/stale queue; approve/reject/batch (M)
- [ ] 5.15 🔒 Propose-never-auto default; auto-migrate is explicit opt-in only (T: default config = propose)
- [ ] 5.16 Analytics: space-by-type/creator/tier, wasting-fast/slow-where-hurts, usage trend (M)

---

## Backend engines (no UI — evidence must be `T:`/`B:`, never `S:`)

> These features are pure backend with no screen to "look at". They are the easiest to falsely mark done, so each internal algorithm gets its own item, verified only by test or benchmark.

### Repository profiling engine
- [ ] BE-R1 Benchmark: warm-up then N sequential + random read/write samples on a temp file in the repo; return median MB/s (B: within ~20% of CrystalDiskMark on the same drive)
- [x] BE-R2 Media-type detection via device query → NVMe/SSD/HDD/Removable/Network; unknown → HDD · T: `DriveProfilerTests` — `DriveProfiler.DetectMediaType` (DriveType + `IOCTL_STORAGE_QUERY_PROPERTY` seek-penalty + bus-type; every failure path falls back to HDD)
- [x] BE-R3 Capacity/free refresh via `DriveInfo` · T: `DriveProfilerTests.Capacity_refresh_matches_drive_info` (matches OS `DriveInfo.TotalSize`) — `DriveProfiler.GetCapacity` (*short-TTL cache is a later optimization*)
- [ ] BE-R4 `VolumeSerial` capture (done) + strict match on re-point (pending) · T: `DriveProfilerTests.Volume_serial_is_read_on_windows` (`GetVolumeInformation`) — re-point mismatch guard still to build
- [x] BE-R5 Tier auto-assign thresholds (configurable, >3000 MB/s→T1, >800→T2, else T3; media-type fallback) · T: `TierPolicyTests` (media-type + speed-threshold cases; removable stays cold) — `TierPolicy.AssignTier`
- [ ] BE-R6 Add-drive rebalance candidate computation (which vars would move) (T)

### Content classification engine
- [x] BE-C1 Precompiled rule set (path-prefix + ext → type); NO per-entry regex recompile · T: `ContentClassificationEngineTests` — `ContentClassificationEngine.Rules` is a single static array of string prefix/suffix checks (zero regex); reused for every entry
- [x] BE-C2 Per-type counters + `isPreset` flags produced per var · T: `ContentClassificationEngineTests.Preset_flags_follow_the_legacy_table` + count tests + `Plugin_count_prefers_cslist_over_cs`
- [x] BE-C3 `PrimaryType` by fixed precedence, deterministic · T: `ContentClassificationEngineTests` (`ChoosePrimary`)
- [x] BE-C4 Gender inference from path fragments + confidence score · T: `GenderInferenceTests` (female/male/futa/auto; male-not-in-female; confidence = vote share)
- [x] BE-C5 `ContentItem` rows keyed on `VarFileId`; `PackageContentCount` derived from canonical · 🔒 T: `IndexingFlowTests.Indexes_a_repository_into_the_catalog` (ContentItems + PackageContentCounts written; canonical VarFile elected, counts follow it) — `EfCatalogStore.ApplyAsync`

### Usage analyzer & classifier
- [x] BE-A1 Signal ingestion: every app activate/load → `UsageEvent` (UTC epoch) · T: `UsageAnalyzerFlowTests` — `EfUsageAnalyzer.RecordAsync` (appends with `clock.UtcNow` epoch ms, `AppObserved`)
- [x] BE-A2 Windowed aggregation (30d/90d) computed time-relative — correct after clock advances with no new events · T: `WindowedUsageTests` — `WindowedUsage.Compute`
- [x] BE-A3 Centrality = reverse-dependency weight · T: `UsageScoringTests` uses `ReverseDependentCount` (maintained by `EfDependencyResolver` on graph change, not usage cadence) as the centrality term (`CentralitySaturation`)
- [x] BE-A4 Score formula = documented weighted blend (recency + frequency + centrality + overrides); known inputs → expected class · T: `UsageScoringTests` (`Recently_used_and_central_scores_hot`, `Never_used_scores_cold`, overrides) — `UsageScoring` (weights in `ScoringConfig`)
- [x] BE-A5 Hysteresis: `Class` flips only past the sealed deadband + cooldown; `LastFlipAt` carried · T: `UsageScoringTests.Hysteresis_cooldown_blocks_an_early_flip` + `Hysteresis_deadband_keeps_class_stable_near_the_boundary`
- [x] BE-A6 Recompute scope = only packages that have usage events · T: `UsageAnalyzerFlowTests` (recompute returns 1 — only the used package) — `EfUsageAnalyzer.RecomputeAsync` iterates distinct event package ids (*strict since-`ComputedAt` delta is a later optimization*)
- [x] BE-A7 Event rollup compaction of >90d events · T: `UsageCompactionTests` — `EfUsageAnalyzer.CompactAsync` (atomic rollup + delete; `UsageStat.RolledUpUseCount` added via migration `AddUsageRollupCount`)
- [x] BE-A8 🔒 Classification reproducible from stored state after DB restore / on a second machine · T: `UsageScoringTests.Score_is_reproducible_from_the_same_inputs` (pure function of persisted inputs: LastUsedAt/Use30d/ReverseDependentCount/pins/LastFlipAt)

### Placement & migration planner
- [x] BE-P1 Desired tier = f(class, policy); diff → misplaced set · T: `PlacementPolicyTests` — `PlacementPolicy.DesiredTier`/`IsMisplaced` (actual tier = VarFile→Repo→Tier at call sites)
- [x] BE-P2 ⚠ Proposal set excludes single-copy, offline members, and removable/network targets · T: `MigrationPlannerTests.Excludes_single_copy_offline_and_unsafe_targets` — `MigrationPlanner`
- [x] BE-P3 ⚠ Free-space reservation ledger for concurrent-job capacity safety · T: `FreeSpaceLedgerTests` — `FreeSpaceLedger` (thread-safe reserve/release, `free − reserved − requested ≥ minFree`)
- [ ] BE-P4 ETA estimate from bytes ÷ target write speed (M)
- [x] BE-P5 🔒 Propose-only by default; auto-execute is explicit opt-in · T: `MigrationPlannerTests.Planner_only_proposes_and_never_returns_an_executed_action` — `MigrationPlanner.Plan` returns proposals only; it has no execute path (execution is a separate, explicitly-invoked step)

### Fingerprint & dedup engine
- [x] BE-F1 🔒 Central-directory reader: Zip64-aware, raw entry-name bytes, dir-entries excluded · T: `ZipCentralDirectoryReaderTests` (`Same_content_different_compression_and_order...`, `Raw_cjk_entry_names_round_trip_without_decoding`, `Corrupt_zip_is_rejected`, `Empty_file_is_rejected`, real-repo probe) — `ZipCentralDirectoryReader` parses EOCD/Zip64 EOCD, yields raw name bytes; engine drops dir entries
- [x] BE-F2 Three signatures (Content / Payload / NoPath) computed in one pass · T: `ContentSignatureEngineTests` (order-independent, dir-excluded, payload-excludes-meta, no-path-ignores-names, mojibake-deterministic) — `ContentSignatureEngine.Compute`
- [x] BE-F3 Lazy full `ContentHash` computed only for verify-before-delete / portability · T: `RealRepoDedupTests.Full_hash_is_deterministic_for_a_real_var` — `Sha256FileHasher` (streaming SHA-256); indexing never calls it (only `DeletionPredicate`/verify paths do)
- [x] BE-F4 Dedup grouping within one `IdentityKey`; cross-identity matches report-only · 🔒 T: `DedupGroupingTests` (`Groups_duplicates_within_one_identity`, `Same_content_across_identities_is_report_only`)

---

## Cross-cutting — safety, ops, portability

- [x] X.1 ⚠ Never hard-delete: all deletes → trash (move, not delete) · T: `FileTrashServiceTests.Trashing_moves_the_file_and_writes_a_manifest` (source moved into trash, not removed) — `FileTrashService.TrashAsync` (*same-volume-move optimization when trash root is co-located*)
- [x] X.2 ⚠ Per-item restore manifest inside trash; restore works with DB absent · T: `FileTrashServiceTests.Restore_works_from_the_manifest_alone_without_a_database` (a fresh service instance restores from `manifest.json` only) + `List_enumerates_trashed_items_from_manifests` (*capacity-aware quota pending*)
- [x] X.3 ⚠ Trash restore returns file to original path · T: `FileTrashServiceTests.Restore_returns_the_file_to_its_original_path` + `Restore_refuses_when_something_occupies_the_original_path` (*re-index hook wires in with the delete orchestrator*)
- [x] X.4 ⚠ Versioned DB backup (consistent `VACUUM INTO` snapshot), kept to the last N · T: `SqliteDatabaseBackupTests.Backup_produces_a_consistent_restorable_copy` (backup opens as its own DB with the data) + `Keeps_only_the_last_n_backups` — `SqliteDatabaseBackup` (*schedule + before-every-destructive-batch triggers wire in with the job scheduler / delete orchestrator*)
- [x] X.5 ⚠ Filesystem-truth reconcile (run after a DB restore, before pending jobs) · T: `ReconcileFlowTests.Reconcile_prunes_vanished_online_files_and_marks_offline_repos` (online repo's vanished file pruned; missing-mount repo → offline, its rows survive) — `CatalogReconciler.ReconcileAsync`
- [ ] X.6 Undo toast on reversible actions (M)
- [ ] X.7 Confirm-destructive modal: reverse-dep check + single-copy protection shown (M)
- [ ] X.8 Onboarding wizard: add drives → benchmark → index → rescue (M/D end-to-end)
- [ ] X.9 Import-from-old-varManager: recognize quarantine dirs + ingest `.fav`/`.hide` sidecars → ContentItemPref (T)
- [ ] X.10 Portable catalog: move DB + shuffle drive letters → app re-finds vars by hash/volume-serial (⚠ D)
- [ ] X.11 Jobs tray: multiple concurrent jobs, per-job progress + pause/cancel (M)
- [ ] X.12 Activity history: every move/install/delete/fix/alias audited (M)
- [x] X.13 Settings persist (VaM path, policies, fix-on-import, …) · T: `EfSettingsServiceTests` (set/get/overwrite upsert, bool fallback, get-all) — `ISettingsService`/`EfSettingsService` over the `Setting` table
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
