# 16 — GUI Implementation Checklist (testable-unit slices, item-detail)

Built from [15-GUI-Completion-Plan.md](15-GUI-Completion-Plan.md) (the 464-item census) and the sealed
prototype [mockups/prototype.html](mockups/prototype.html). **One slice = one testable unit** — a shared
component, a backend facade, a screen, or a dialog. A unit is DONE only when every item it lists is built,
its headless test is green, and it matches the draft with zero drift.

## Global hard rules (apply to EVERY slice)

- **HR-0 · Recall before code (anti-drift).** Before writing any code for a unit, **read the cited
  `prototype.html` line range** and this unit's item list into memory. Implement *exactly* those items —
  none invented, none dropped. If reality forces a deviation from the draft, stop and flag it; do not
  silently drift.
- **HR-1 · Layering.** The App references only Common + Sdk + Host. Bind **SDK interfaces only** — never
  Domain, Infrastructure, or EF types. Missing backend ⇒ do the matching **BE-Nx** slice first.
- **HR-2 · MVVM.** CommunityToolkit (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`,
  `partial void On…Changed`). No logic in code-behind beyond view construction.
- **HR-3 · Shared visuals.** Reuse Section-0 components; do not re-style a card/table/chip/tab locally.
- **HR-4 · Test gate.** Every unit ships an `[AvaloniaFact]` headless test (screens/dialogs) or xUnit unit
  test (VMs/facades) that constructs the unit with a stub/fake service and asserts **each listed item**
  renders/behaves. No sleep-then-assert; `Dispatcher.UIThread.RunJobs()`.
- **HR-5 · Theme + colourblind.** Light+dark via theme tokens (SC-1); state encoded by text/shape, not
  colour alone.
- **HR-6 · Threading + errors.** Catalog writes via `IWriteQueue`; long ops via `IJobQueue` (progress +
  cancel); expected failures return `Result`/`Result<T>`, never exceptions.
- **HR-7 · States.** Every data unit handles loading / empty / error explicitly.
- **HR-8 · DoD.** Tick a unit only when: all listed items present · draft matched (HR-0) · headless test
  green · self-review clean. Cite the test name next to the tick.

## Slice order (dependency-first)

`Section 0 shared components → Section 1 backend enablement (BE-N) → Section 2 shell spine → Section 3
screens → Section 4 dialogs → Section 5 polish`. Within a section, lower ids first.

---

## Section 0 — Shared components (SC)
Build once, reused everywhere. Read the CSS token block `prototype.html:1–195` once before SC-1.

- [x] **SC-1 · Theme tokens & palette.** *Read:* `prototype.html:1–195` (`:root` vars).
  *Items:* accent/accent-hi/accent-bg/accent-line; temp hot/warm/cold; good/warn/crit (+bg); tier colours;
  bg-0..3+hover, border/border-hi, text hi/lo/muted; radius/font-sizes/mono. Light + dark variants.
  *Rule:* all later units read these tokens; no raw hex in unit views. *Done:* `App.axaml` Light/Dark/Default
  `ThemeDictionaries` (23 colours + brushes + non-colour tokens) · T: `ThemeTokensTests`
  (`Every_colour_token_resolves_in_light_and_dark`, `Every_brush_token_resolves`,
  `Light_and_dark_variants_actually_differ`, `Non_colour_tokens_resolve`).
- [x] **SC-2 · Card.** *Read:* any `.card` (e.g. `:244`). Padded container + `<h3>` title slot. *Done:*
  `Controls/Card.cs` + `Themes/Controls.axaml` ControlTheme (bg-1/border/radius/14px, optional header). T:
  `CardControlTests` (`Card_renders_header_and_content`, `Card_without_header_hides_the_title`).
- [x] **SC-3 · DataTable.** *Read:* `:335–343` (thead) + JS `:673–693` (row template).
  *Items:* checkbox/multi-select, sortable header (asc/desc caret, single active col), virtualized rows,
  numeric/right-align cells, per-row action slot (consumer RowTemplate). *Rule:* server-paged — header
  click raises `SortCommand` (not client sort); `VirtualizingStackPanel` body. *Done:* `Controls/DataTable.cs`
  + `DataTableColumn.cs` + ControlTheme. T: `DataTableControlTests`
  (`Header_click_raises_sort_command_with_the_column_key`, `Active_caret_reflects_sort_key_and_direction`,
  `Body_virtualizes_a_large_list` (realized<200 of 5000), `Multi_select_populates_selected_items`).
- [x] **SC-4 · Tabs.** *Read:* `:445,462,479,505,543,565`. Single active tab, count badges. *Done:*
  `Controls/Tabs.cs` (+ `TabItemModel`) + ControlTheme — `SelectedIndex`/`SelectedItem` seam, count badges,
  active styling. T: `TabsControlTests` (`Click_switches_active_tab_and_selected_item`, `Count_badges_render`,
  `Selected_item_swaps_when_index_changes`).
- [x] **SC-5 · Chip / Tag / StatePill.** *Read:* `.chip`(`:323`), `.tag`(`:362`), `.st`(`:381` ok/sub/miss).
  *Rule:* state via text+shape (HR-5). *Done:* `Controls/Pills.cs` (`StatePill`+`Chip`+`Tag`, class-driven
  ControlThemes). T: `PillsControlTests` (`State_pill_carries_meaning_in_text_per_state`,
  `State_pill_defaults_text_to_state_name`, `Chip_active_toggles_class`, `Tag_kind_sets_class`).
- [x] **SC-6 · Bars (progress / capacity / stacked).** *Read:* `.bar`(`:246`), stacked(`:267`), job bar(`:235`).
  *Done:* `Controls/Bars.cs` (`MeterBar` value-tracking fill via star cols; `StackedBar` proportional
  segments; no animation → reduced-motion safe). T: `BarsControlTests` (`Meter_fill_tracks_value`,
  `Meter_clamps_out_of_range_value`, `Stacked_renders_proportional_segments`).
- [x] **SC-7 · StatTile / TempDot.** *Read:* `.stat`(`:261`), `.temp`(`:246`). Big number + unit; hot/warm/cold dot.
  *Done:* `Controls/Stat.cs` (`StatTile` value+unit+brush; `TempDot` class-per-temperature). T:
  `StatControlTests` (`Stat_tile_renders_value_and_unit`, `Temp_dot_classes_by_temperature`).
- [x] **SC-8 · SearchableCombo.** *Read:* `:318–321` + JS `:720–732`.
  *Items:* button (label+caret), search input, count-annotated filtered list, keyboard ↑/↓/Enter/Esc, empty
  state. *Rule:* type-to-filter (client). *Done:* `Controls/SearchableCombo.cs` (+ `ComboOption`) + Popup
  ControlTheme; `Refilter`/`MoveHighlight`/`CommitHighlighted`/`HandleKey` logic seam. T:
  `SearchableComboTests` (filter narrows, empty state, highlight+clamp+enter, keyboard map, escape closes).
- [x] **SC-9 · ModalHost / Dialog base.** *Read:* `.modal-bg`/`.modal`(`:581`) + JS `:708–710,734`.
  *Items:* backdrop, header+×, body slot, footer buttons; open/close; **Esc closes**; backdrop-click closes;
  focus into dialog on open. *Rule:* one host, dialogs are content. *Done:* `Controls/ModalHost.cs` +
  ControlTheme. T: `ModalHostTests` (open shows backdrop/title/content; closed hides; `Escape_closes`;
  `Close_button_closes`; `Backdrop_click_closes` via headless mouse).
- [x] **SC-10 · Toast (undo).** *Read:* `:668` + JS `:715–716`. Message + optional Undo + auto-dismiss.
  *Done:* `Controls/Toast.cs` (`Show`/`Undo`/`Dismiss`, injectable dismiss delay) + ControlTheme. T:
  `ToastControlTests` (message+undo render; no-undo hides button; Undo fires callback+hides; gated
  auto-dismiss hides).
- [x] **SC-11 · RailNavItem.** *Read:* `:202–220`. Icon + label + optional badge + active state. *Done:*
  `Controls/RailNavItem.cs` (icon Content, Label, Badge+BadgeKind, IsActive) + ControlTheme. T:
  `RailNavItemTests` (`Active_toggles_class`, `Badge_count_renders_when_present`, `No_badge_hides_the_badge`).
- [x] **SC-12 · JobRow.** *Read:* `:235–237`. Title + cancel × + progress bar + sub-line. *Done:*
  `Controls/JobRow.cs` (binds `JobHandle`; `Refresh` re-reads the non-observable handle; MeterBar fraction).
  T: `JobRowTests` (`Binds_handle_title_progress_and_subline`, `Cancel_button_cancels_the_job_token`,
  `Refresh_repicks_updated_progress`).

---

## Section 1 — Backend enablement (BE-N)
Each = new SDK interface + Infrastructure impl + DI registration + **integration test that drives the
engine from the SDK boundary** (closes the "engine never runs" gap; see 15-plan §Engine audit). Algorithms
are specified in 15-plan §New backend tasks catalog — read that entry before coding each.

- [x] **BE-N0 · Index orchestration + trigger.** `IIndexOrchestrator.IndexAllAsync/IndexRepositoryAsync`;
  after index → `ResolveAllAsync` → `IUsageAnalyzer.RecomputeAsync`. **Fixed** `EfCatalogStore.RefreshOneAsync`
  `HasMissingDeps` clobber (now recomputed from the canonical var's `Dependency.IsMissing`). *Done:* SDK
  `IIndexOrchestrator` + `Modules.Indexing/IndexOrchestrator` (scope-factory pattern, registered singleton).
  T: `IndexOrchestratorFlowTests.Index_all_populates_read_model_resolves_and_recomputes` (missing-dep var →
  `HasMissingDeps=true`) + `Index_all_on_a_real_repo` (D: 277-var corpus; E/F/G reserved as move targets).
- [x] **BE-N1 · `IDashboardService`.** *Done:* SDK `IDashboardService`/`DashboardSummary`/`TierUtilization`
  + `Infrastructure/Library/EfDashboardService` (totals, class counts, active, missing-deps, per-tier
  storage from repos), registered. T: `EfDashboardServiceTests` (`Summary_aggregates_totals_classes_and_tiers`,
  `Empty_catalog_returns_zeros`). *(reclaim tiles compose BE-N4/N2 at the Dashboard screen.)*
- [x] **BE-N2 · `ITieringService`** (PlacementPolicy/MigrationPlanner). *Done:* SDK `ITieringService`
  (`TierClassCounts`/`MisplacedItem`/`TierMigrationPlan`) + `EfTieringService` (class counts, misplaced via
  `PlacementPolicy.IsMisplaced`, propose-only plan via `MigrationPlanner.Plan` with safe-tier predicate),
  registered. T: `EfTieringServiceTests` (`Class_counts_and_misplaced_from_read_model`,
  `Build_plan_proposes_moves_and_excludes_single_copy`).
- [x] **BE-N3 · `IMigrationService`** (MigrationRunner/DurableFileMover). *Done:* SDK `IMigrationService`
  (`MigrationRequest`/`MigrationRunResult`) + `EfMigrationService` (persists a `MigrationJob` per move, runs
  the durable state machine), registered. T: `MigrationServiceFlowTests.Run_moves_the_var_to_the_target_and_trashes_the_source`
  + `Cross_drive_move_between_reserved_repos` (real E:→F: move on the reserved repos, non-destructive scratch file).
- [x] **BE-N4 · `IReclaimService`** (DedupGrouping/DeletionPredicate/IFileHasher). *Done:* SDK
  `IReclaimService` (`DuplicateGroup`/`DuplicateCopy`/`ReclaimResult`) + `EfReclaimService` (exact groups;
  lazy hash then `DeletionPredicate.Evaluate` gates each trash via `ITrashService`), registered. T:
  `ReclaimServiceFlowTests.Groups_exact_duplicates_and_trashes_only_redundant_verified_copies` (3 dups →
  trash 2, keep 1; single-copy blocked).
- [x] **BE-N5 · `IHealthService`** (EncodingHealthEngine/EncodingFixCoordinator). *Done:* SDK `IHealthService`
  (`EncodingGroup`/`IntegrityIssue`) + `EfHealthService` (groups by codepage, integrity list, `FixAsync`
  computes the `.fixed.var` sibling path → `EncodingFixCoordinator`), registered. T:
  `HealthServiceFlowTests.Encoding_groups_then_fix_creates_utf8_var` (GBK group; fix creates UTF-8 sibling,
  original retained).
- [x] **BE-N6 · `ITrashQueryService`** (ITrashService/SqliteDatabaseBackup). *Done:* SDK `ITrashQueryService`
  (`TrashItemDto`/`BackupDto`) + `EfTrashQueryService`; added `ITrashService.PurgeAsync` + `FileTrashService`
  impl (deletes the trash item dir); backup dir derived from the DB path. Registered. T:
  `TrashQueryServiceFlowTests.List_restore_purge_and_backup_round_trip`.
- [x] **BE-N7 · `IProfileService`** (IVamProfileService). *Done:* SDK `IProfileService` + `EfProfileService`
  (VaM root from `SettingKeys.VamPath`; list/create/active/switch delegate to `IVamProfileService`),
  registered. T: `ProfileServiceFlowTests.Create_list_and_switch_profiles` (switch skips on `symlink.privilege`)
  + `List_is_empty_without_vam_path`.
- [x] **BE-N8 · `IProposalService`** (aggregates N2/N4/N5/stale). *Done:* SDK `IProposalService`
  (`Proposal`/`ProposalKind`/`ProposalActionResult`) + `EfProposalService` — lists Rebalance/Dedup/
  EncodingFix/RetireStale; Approve dispatches to migration/reclaim/health/trash; Reject records. Registered.
  T: `ProposalServiceFlowTests.Lists_proposals_and_approve_dispatches` (dedup approve trashes redundant;
  stale approve retires old version; reject ok).
- [x] **BE-N9 · `IPackageDetailQuery`** (IDependencyGraph fwd, ContentItem, VarFile lineage). *Done:* SDK
  `IPackageDetailQuery`/`PackageDetail`/`ContentItemDto`/`CopyDto` + `EfPackageDetailQuery` (identity/license/
  class, `ReverseDependentCount`, `ForwardClosureAsync`, canonical content items, copies+lineage). Registered.
  T: `EfPackageDetailQueryTests` (`Returns_copies_content_items_and_forward_closure`, `Missing_package_returns_null`).
- [x] **BE-N10 · `ILibraryActionService`** (preset/fix/delete/txt). *Done:* SDK `ILibraryActionService`
  (`BulkActionResult`) + `EfLibraryActionService`: AddToPreset (`IPresetService`), FixEncoding
  (`IHealthService`), Delete (gated by `DeletionPredicate` → `ITrashService`), ExportTxt. Registered. T:
  `LibraryActionServiceFlowTests.Add_to_preset_export_and_predicate_gated_delete` (dup deletes, single-copy
  blocked). *(Install/Uninstall/Move wired at the ops-bar screen SCR-2e via activation/migration.)*
- [x] **BE-N11 · `IAliasService`** (VarAlias). *Done:* SDK `IAliasService`/`AliasDto` + `EfAliasService`
  (folds `MissingRefKey` via `IdentityFold` to match dep keys; set/list/remove). The resolver already
  consults `VarAliases`. Registered. T: `AliasServiceFlowTests.Alias_makes_a_missing_dependency_resolve`
  (missing before → set alias → re-resolve → not missing).
- [x] **BE-N12 · Library facets** (`ITagService`; extend `LibraryQuery`). *Done:* `LibraryQuery` +
  `PackageName`/`InstalledOnly`/`SingleCopyOnly`/`Types`/`Tiers` wired in `EfLibraryQueryService`; SDK
  `ITagService`/`TagInfo` + `EfTagService` (create/tag/untag/list-with-counts/package-ids). Registered. T:
  `LibraryFacetsTests` (`Facet_filters_narrow_the_page`, `Tag_create_apply_list_and_query`). *(Saved views =
  client-side query presets; `ICollectionService` follows the same shape at the rail screen.)*
- [x] **BE-N13 · Onboarding/add-repo** (Register/Benchmark + reserveBytes + BE-N0). *Done:* SDK
  `IOnboardingService`/`OnboardingResult` + `EfOnboardingService` (register → set reserve → orchestrator
  index). Registered. T: `OnboardingServiceFlowTests.Add_and_index_registers_detects_and_populates` (tier
  assigned, catalog populated) + `Add_and_index_a_real_repo` (D: corpus).
- [x] **BE-N14 · Command palette / search** (action registry + `LibraryQuery.SearchText`). *Done:* SDK
  `ICommandPaletteService`/`CommandHit` + `EfCommandPaletteService` (built-in nav/action registry filtered
  by substring + package search via `ILibraryQueryService`). Registered. T:
  `EfCommandPaletteServiceTests.Search_returns_nav_action_and_package_hits`.

---

## Section 2 — Shell spine (SH)

- [x] **SH-1 · ShellViewModel + navigation.** *Done:* `ViewModels/ShellViewModel.cs` — `ActiveScreen`/
  `ActiveScreenId` + `NavigateCommand`, grouped `AllScreens` rail list (13, Browse/Optimize/Problems/System),
  screen VMs supplied as a DI map. T: `ShellViewModelTests` (`Navigate_swaps_active_screen`,
  `Rail_lists_all_13_screens_in_four_groups`, `Navigate_to_unregistered_screen_sets_id_but_null_screen`).
- [x] **SH-2 · Rail nav.** *Read:* `:196–222`. *Items:* 13 nav + 4 group labels + 3 badges (uses SC-11).
  *Done:* `Views/RailView.axaml` (4 grouped `ItemsControl`s over `Browse/Optimize/Problem/SystemItems`,
  each a `RailNavItem` in a nav Button → `NavigateCommand`); `ShellViewModel.RailItems`/`SetBadge`. T:
  `RailViewTests` (`Renders_13_nav_items_and_group_headers`, `Badges_bind_to_problem_screens`,
  `Clicking_a_rail_item_navigates`).
- [x] **SH-3 · Top bar.** *Read:* `:225–232`. *Items:* search, rescue, jobs+dot, add-repo, theme. *Done:*
  `Views/TopBarView.axaml` + shell commands (`ToggleJobs`/`OpenPalette`/`AddRepo`/`Rescue`/`ToggleTheme`,
  `HasActiveJobs` dot, `SearchText`). T: `TopBarViewTests` (`Renders_controls_and_binds_commands`,
  `Jobs_dot_reflects_active_jobs`, `Toggle_jobs_and_theme_commands_work`).
- [x] **SH-4 · Jobs panel.** *Read:* `:233–238`. *Items:* header, pause-all, JobRows (SC-12). *Done:*
  `Views/JobsPanelView.axaml` (ItemsControl of `JobRow` over `ActiveJobs`, pause-all) + shell
  `ActiveJobs`/`RefreshJobs`/`PauseAllCommand`. T: `JobsPanelViewTests`
  (`Panel_renders_job_rows_with_progress`, `Pause_all_cancels_every_job`).
- [x] **SH-5 · Log dock.** *Read:* `:576`. *Items:* index status, selected count, tier %. *Done:*
  `Views/LogDockView.axaml` + shell `IndexStatus`/`SelectedCount`/`TierSummary`. T:
  `LogDockViewTests.Binds_index_selected_and_tier_summary`.
- [x] **SH-6 · AppHost wiring.** *Done:* `Composition/AppHost.CreateShell(services)` builds the shell from
  the DI provider — real VMs for Library/Analytics/Activity/Missing, `PlaceholderScreenViewModel` for
  screens whose views land in SCR slices; `TryCreateShell()` composes the host. T: `AppHostShellTests`
  (`Shell_resolves_with_all_screens_non_null` — 13 non-null; `Backend_ready_screens_use_real_view_models`).
  *(MainWindow→shell-layout swap happens once screen views exist.)*

---

## Section 3 — Screens (SCR)
Each cites its plan item numbers (15-plan) and its HTML range. Read both before coding (HR-0).

- [x] **SCR-1 · Dashboard.** *Read:* `:241–286`. *BE:* N1 (+ N2/N4/N8 tiles compose later). *Done:*
  `DashboardViewModel(IDashboardService)` + `Views/DashboardView.axaml` (header stat, storage-by-tier,
  classification, attention cards). T: `DashboardScreenTests` (`Load_populates_summary`,
  `View_renders_totals_and_classification`).
- [x] **SCR-2 · Library.** *Read:* `:288–392` + JS `:673–704`. *BE:* `ILibraryQueryService`, N10, N12. *Done:*
  `Views/LibraryView.axaml` bound to `LibraryViewModel` (extended with `PackageNameFilter`, ops
  `ExportSelectedCommand` via BE-N10, rail `ShowAll`/`ShowFavorites`). T: `LibraryScreenTests`
  (`Renders_table_rows_and_detail`, `Export_selection_produces_txt`, `Rail_favorites_filters`).
  - [x] SCR-2a Rail · [x] SCR-2b Facet bar · [x] SCR-2c Table cols · [x] SCR-2d Gallery · [x] SCR-2e Ops bar
    (Export functional; Delete/Fix/Add-to-preset buttons route through DLG-6/preset picker) · [x] SCR-2f Detail panel.
- [x] **SCR-3 · Repositories.** *Read:* `:395–414`. *BE:* `IRepositoryService`. *Done:*
  `RepositoriesViewModel` + `Views/RepositoriesView.axaml` (repo cards: tier/name/online/media/path,
  Benchmark). Wired in AppHost. T: `RepositoriesScreenTests.Loads_and_renders_repo_cards`.
- [x] **SCR-4 · Loading presets.** *Read:* `:417–440`. *BE:* `IPresetService`, N7. *Done:* `PresetsViewModel`
  + `Views/PresetsView.axaml` (preset list, activation preview via `PreviewActivationAsync`, Switch via
  `IProfileService`). Wired in AppHost. T: `PresetsScreenTests.Lists_presets_and_previews_selection`.
- [x] **SCR-5 · Tiering & migration.** *Read:* `:443–457`. *BE:* N2. *Done:* `TieringViewModel` +
  `Views/TieringView.axaml` (class-count cards, misplaced table, build-plan). Wired in AppHost. T:
  `TieringScreenTests.Shows_counts_and_misplaced`.
- [x] **SCR-6 · Duplicates & reclaim.** *Read:* `:460–474`. *BE:* N4. *Done:* `DupesViewModel` +
  `Views/DupesView.axaml` (reclaimable total, exact-dup group list, trash-group). Wired in AppHost. T:
  `DupesScreenTests.Lists_groups_and_reclaimable`.
- [x] **SCR-7 · Analytics.** *Read:* `:521–538`. *BE:* `IAnalyticsService`. *Done:* `Views/AnalyticsView.axaml`
  bound to existing `AnalyticsViewModel` (space-by-type + space-by-creator cards). Wired in AppHost. T:
  `AnalyticsScreenTests.Renders_space_breakdowns`.
- [x] **SCR-8 · Proposals & review.** *Read:* `:503–518`. *BE:* N8. *Done:* `ProposalsViewModel(IProposalService)`
  (rewritten from placeholder to bind SDK) + `Views/ProposalsView.axaml` (proposal cards, approve→dispatch,
  reject→remove). Wired in AppHost. T: `ProposalsViewModelTests.Loads_then_approve_and_reject_remove_from_pending`
  + `ProposalsScreenTests.Renders_proposal_cards`.
- [x] **SCR-9 · Health & fix.** *Read:* `:477–490`. *BE:* N5. *Done:* `HealthViewModel(IHealthService)` +
  `Views/HealthView.axaml` (encoding groups by codepage, integrity list, fix). Wired in AppHost. T:
  `HealthScreenTests.Lists_encoding_groups`.
- [x] **SCR-10 · Missing deps.** *Read:* `:493–500`. *BE:* `IMissingDepsQuery`. *Done:* `Views/MissingView.axaml`
  bound to existing `MissingDepsViewModel` (missing-ref table, resolve). Wired in AppHost. T:
  `MissingScreenTests.Lists_missing_refs`.
- [x] **SCR-11 · Trash & backup.** *Read:* `:541–549`. *BE:* N6. *Done:* `TrashViewModel(ITrashQueryService)`
  + `Views/TrashView.axaml` (trash table restore/purge, backup-now). Wired in AppHost. T:
  `TrashScreenTests.Lists_trash_items`.
- [x] **SCR-12 · Activity history.** *Read:* `:552–560`. *BE:* `IActivityLog`. *Done:* `Views/ActivityView.axaml`
  bound to existing `ActivityViewModel` (audit table: when/kind/detail). Wired in AppHost. T:
  `ActivityScreenTests.Lists_audit_records`.
- [x] **SCR-13 · Settings.** *Read:* `:563–573`. *BE:* `ISettingsService`. *Done:* `SettingsViewModel` +
  `Views/SettingsView.axaml` (VaM path, fix-on-import, Save). Wired in AppHost. T:
  `SettingsScreenTests.Loads_and_saves_vam_path`. *(All 13 screens now real VMs — no placeholders.)*

---

## Section 4 — Dialogs / modals (DLG)
Each is content inside SC-9. Read its exact modal lines (HR-0).

- [x] **DLG-1 · m-onboard.** *Read:* `:581–589`. *BE:* N13. *Done:* `OnboardingViewModel` extended
  (FolderPath, `AddAndIndexCommand` via `IOnboardingService`) + `Views/OnboardingDialog.axaml` (stepper,
  folder input, add&index). T: `OnboardingDialogTests.Add_and_index_reports_result_and_completes`.
- [x] **DLG-2 · m-addrepo.** *Done:* `AddRepoViewModel(IRepositoryService)` + `Views/AddRepoDialog.axaml` (folder, detected media/tier, reserve, add). T: `AddRepoDialogTests.Add_registers_and_reports_detected_tier`.
- [x] **DLG-3 · m-migrate.** *BE:* N2/N3. *Done:* `MigrateViewModel(ITieringService)` + `Views/MigrateDialog.axaml` (plan summary, copy→verify→rename→delete flow, single-copy exclusion warning). T: `MigrateDialogTests.Loads_plan_and_flags_exclusions`.
- [ ] **DLG-4 · m-fix.** *Read:* `:609–617`. *Items:* fix #1–7. *BE:* N5. *Test:* before/after preview; apply-to-group.
- [ ] **DLG-5 · m-alias.** *Read:* `:619–626`. *Items:* alias #1–6. *BE:* N11. *Test:* map+scope; Save persists alias.
- [ ] **DLG-6 · m-confirm (delete).** *Read:* `:628–636`. *Items:* confirm #1–7. *BE:* N10, `DeletionPredicate`, N9 reverse. *Rule:* single-copy **protected/excluded**; reverse-dep count shown; → trash. *Test:* protected row cannot be deleted; safe items go to trash.
- [ ] **DLG-7 · m-preset (edit).** *Read:* `:638–645`. *Items:* preset #1–12. *BE:* `IPresetService`, N10. *Test:* member table + pin; Save persists.
- [ ] **DLG-8 · m-dupe (review).** *Read:* `:647–654`. *Items:* dupe #1–8. *BE:* N4. *Rule:* keep-one; deletes full-hash verified first. *Test:* keep radio + trash-rest reclaim.
- [ ] **DLG-9 · m-rescue.** *Read:* `:656–660`. *Items:* rescue #1–5. *BE:* `IActivationService.RescueAsync`. *Test:* baseline select; Apply calls rescue.
- [ ] **DLG-10 · m-vardetail.** *Read:* `:662–666`. *Items:* vardetail #1–12. *BE:* N9. *Test:* 4 tabs (Overview/Dep-graph/Content/Copies) + kv fields bind detail.
- [ ] **DLG-11 · Toast/undo.** Covered by SC-10; wire per-action undo (trash restore / activation revert). *Test:* Undo reverses the last action.

---

## Section 5 — Polish (POL)

- [ ] **POL-1 · Theme sweep.** Every screen/dialog uses SC-1 tokens in Light + Dark; audit contrast.
- [ ] **POL-2 · Keyboard + focus.** Tab order, visible focus, Ctrl-K palette, Esc everywhere.
- [ ] **POL-3 · States everywhere.** Loading/empty/error on every data unit.
- [ ] **POL-4 · Shell smoke test.** Launch the shell headless, visit all 13 screens + open each dialog, assert no bind errors and real data.

## Definition of done for the GUI

All Section 0–4 units ticked (each with a green headless/integration test and zero draft drift), the shell
launches and every one of the 464 items is present and bound to a real backend, and POL-4 passes. At that
point the engines built earlier finally **run** through the UI, not just the test harness.
