# 18 — GUI Gap Implementation Checklist (close the audit gap)

Built from the source-vs-prototype audit [17-GUI-Completeness-Audit.md](17-GUI-Completeness-Audit.md) and the
sealed prototype [mockups/prototype.html](mockups/prototype.html). Where `16` ticked skeletons, **this list
closes the real gap**: dead wiring, unreachable dialogs, missing controls, missing columns, missing tabs.
**One slice = one testable unit.** A unit is DONE only when every item it lists is built **and wired to a real
SDK source**, its headless/unit test is green, and it matches the draft with zero drift.

## Global hard rules (inherit all of `16`'s HR-0…HR-8, plus these gap rules)

- **HR-G0 · Wired, not drawn.** A control counts as done only when bound to a real command/data source. A
  button with no `Command`, or a VM property nothing assigns, is **NOT done** — that is exactly what this
  checklist exists to fix. Every unit's test must prove the wire (command executes / property populates from a
  fake service), not just that the element renders.
- **HR-G1 · Reachability.** Any dialog/modal must be **openable from its trigger**. The unit test asserts:
  invoking the trigger command makes the modal visible, and Close/Esc/backdrop hides it. An orphaned
  `UserControl` is not done.
- **HR-G2 · Recall the audit line too.** Before coding a unit, read its cited `prototype.html` range **and**
  the matching bullet in `17`'s §B/§C so you build the *exact* missing items, not a fresh reinterpretation.
- **HR-G3 · Reuse Section-0.** `SC-1…SC-12` controls already exist (Card, DataTable, Tabs, Pills, Bars,
  StatTile/TempDot, SearchableCombo, ModalHost, Toast, RailNavItem, JobRow). Adopt them — do **not** hand-roll
  a Grid where `DataTable`/`Tabs`/`SearchableCombo` is the specified control.
- **HR-G4 · Verify SDK surface first.** Before a screen/dialog unit, confirm the SDK method it needs exists. If
  absent, do its **BE-G** unit first (HR-1). Do not fake data in the VM.

## Slice order
`G-A systemic enablement → BE-G backend gaps → G-C shell chrome → G-D screens → G-E dialogs → G-F polish.`
Within a section, lower ids first. **G-A unblocks everything — do it first.**

---

## Section G-A — Systemic enablement (dead wiring → live) · fixes `17` §A

- [x] **GA-1 · Dialog service + ModalHost host.** *Read:* `prototype.html:182–193` (modal CSS), `:708–710`
  (open/close), `17` §A.1. *Items:* an `IDialogService` (App-level) with `Show(vm)`/`Close()`; a single
  `ModalHost` placed in `MainWindow` bound to the service's current dialog VM + `IsOpen`; a VM→dialog-view
  `DataTemplate` map for all 10 dialogs; Esc + backdrop close (ModalHost already supports). *Rule:* screens
  raise "open dialog X" through the service; no screen news-up a `Window`. *Test:* `DialogServiceTests`
  (`Show_makes_modal_visible_and_hosts_the_vm`, `Close_and_Esc_hide_it`, `Each_of_10_dialog_vms_maps_to_a_view`).
- [x] **GA-2 · Live job feed.** *Read:* `:233–238`, `17` §A.2. *Items:* `AppHost` subscribes/polls `IJobQueue`
  and calls `ShellViewModel.RefreshJobs(...)`; panel shows one `JobRow` per active job with progress + cancel;
  unread dot + `HasActiveJobs` reflect the live count. *Test:* `JobFeedTests`
  (`Active_jobs_from_the_queue_render_as_rows`, `Empty_queue_shows_no_jobs_and_clears_dot`,
  `Cancel_on_a_row_cancels_the_job`).
- [x] **GA-3 · Rail badge feed.** *Read:* `:212–214` (Proposals/Health/Missing badges), `17` §A.2. *Items:*
  compute counts from `IProposalService`/`IHealthService`/`IMissingDepsQuery` and call `SetBadge`; refresh on
  the relevant domain events. *Test:* `RailBadgeFeedTests` (`Badges_populate_from_services`,
  `Zero_count_clears_the_badge`).
- [x] **GA-4 · Log-dock feed.** *Read:* `:576`, `17` §A.2. *Items:* bind `IndexStatus` to the running index
  job, `SelectedCount` to the active screen's selection, `TierSummary` to `IDashboardService` tier fill;
  animated spinner while indexing. *Test:* `LogDockFeedTests` (`Index_status_reflects_running_job`,
  `Tier_summary_reflects_storage`, `Selected_count_tracks_selection`).
- [x] **GA-5 · Startup index trigger.** *Read:* `17` §A.3, `IIndexOrchestrator`. *Items:* on launch (or when
  the catalog is empty / a repo is added), App enqueues `IIndexOrchestrator.IndexAllAsync` via `IJobQueue`
  (progress → GA-2/GA-4). *Rule:* never block the UI thread; cancellable. *Test:* `StartupIndexTests` unit +
  **E2E** `Launching_over_a_real_repo_indexes_and_populates_library` against `D:\VarVault_test_repo`.
- [x] **GA-6 · Top-bar + quick-action handlers.** *Read:* `:228–231`, `242`, `279–283`, `17` §A.2/§B.
  *Items:* wire `RescueHandler`→open Rescue dialog, `AddRepoHandler`→open Add-repo dialog; Dashboard
  Quick-actions + Setup-wizard + Rescue buttons; global search `Enter`→command palette (`ICommandPaletteService`).
  *Test:* `TopBarWiringTests` (`Rescue_opens_the_rescue_dialog`, `Add_repo_opens_the_add_repo_dialog`,
  `Search_enter_opens_the_palette`).

---

## Section BE-G — Backend gaps (add only if the SDK method is absent; verify per HR-G4)

- [x] **BE-G1 · Library item operations.** *Done:* extended `ILibraryActionService` with `MoveToSubfolderAsync`
  (durable same-volume move + catalog RelativePath update) and `ResolveTxtAsync` (install-from-txt intake →
  owned ids + unmatched). Delete/AddToPreset/FixEncoding/ExportTxt already existed. *Deviation (HR-0):*
  `Install`/`Uninstall` are per-package profile-activation ops — they belong to the preset/activation engine
  (`IActivationService` operates on presets, not ad-hoc packages); wired at the ops-bar as add/remove-from-preset
  rather than a new symlink path, to avoid drifting the sealed "install = symlink" semantics. *Test:*
  `GapBackendFlowTests` (`Move_to_subfolder_relocates_the_file_and_updates_the_catalog`,
  `Resolve_txt_matches_owned_and_reports_unmatched`).
- [x] **BE-G2 · Repository management.** *Done:* `BenchmarkAsync` already existed; added `SetTierAsync` (manual
  T1/T2/T3 override, persisted). *Deviation:* per-repo reserve is a settings value (not on `RepositoryInfo`);
  edited via Settings, not the card. *Test:* `GapBackendFlowTests.Set_tier_persists_the_manual_override`.
- [x] **BE-G3 · Preset editing.** *Done:* added `RemoveMemberAsync` + `MembersAsync`; `AddMemberAsync`/`CreateAsync`
  already existed; deactivate-all = `IActivationService.RescueAsync`. *Test:*
  `GapBackendFlowTests.Preset_remove_member_and_members_reflect_edits`.
- [x] **BE-G4 · Catalog-backups query + restore.** *Done:* already on `ITrashQueryService`
  (`ListBackupsAsync`/`BackupNowAsync`/`RestoreAsync`). *Test:* `GapBackendFlowTests.Backups_list_and_backup_now_work`.
- [x] **BE-G5 · Tiering simulate-policy.** *Done:* `ITieringService.BuildPlanAsync` already returns a propose-only
  `TierMigrationPlan` (nothing moves) — that is the simulate/dry-run. *Test:*
  `GapBackendFlowTests.Tiering_plan_is_propose_only_simulate`.
- [x] **BE-G6 · Var-detail deep data.** *Done:* `IPackageDetailQuery.GetAsync` → `PackageDetail` already carries
  `ForwardClosure`, `DependedOnByCount` (reverse), `ContentItems`, `Copies` (with `FixedFromVarFileId` lineage).
  Covered by existing `ForwardClosureFlowTests`/detail query tests.

---

## Section G-C — Shell chrome · fixes `17` §A.4/§A.5

- [x] **GC-1 · Rail branding + icons.** *Read:* `:198–220`. *Items:* logo block ("V" gradient tile) + "VarVault"
  + live package-count subtitle; per-item SVG icon (13). *Test:* `RailChromeTests`
  (`Logo_and_package_count_render`, `Every_nav_item_has_an_icon`).
- [x] **GC-2 · Tab adoption sweep.** *Read:* `:445,462,479,505,543,565,663`. *Items:* place `SC-4 Tabs` on
  Tiering(4)/Dupes(4)/Health(3)/Proposals(5)/Trash(2)/Settings(5)/Var-detail(4) with the exact tab labels;
  each tab switches the sub-content. (Per-tab *content* lands in the screen units below.) *Test:*
  `TabAdoptionTests` (`Each_surface_shows_its_named_tabs`, `Tab_click_swaps_content`).

---

## Section G-D — Screen item completion (one unit per screen) · fixes `17` §B
Each unit: adopt Section-0 controls, add the listed missing items, wire every action to its SDK/BE-G source.

- [ ] **GD-1 · Dashboard complete.** *Read:* `:241–284`, `17` §B/Dashboard. *Add:* temp dots + tier labels +
  rebalance link on Storage card; stacked bar + "tune →" on Classification; 4 clickable attention rows
  (icon+title+desc+action, navigate); **Reclaimable-space** card (StatTile 590GB + breakdown + wizard btn);
  **Recent-activity** card (3 rows + full-history link); **Quick-actions** card (3 wired buttons); screen-head
  Setup-wizard + Rescue. *Test:* `DashboardViewTests` (`Six_cards_render`, `Attention_row_navigates`,
  `Quick_action_navigates`).
- [ ] **GD-2 · Library left rail.** *Read:* `:290–314`. *Add:* 6 saved views w/ dot+count (All, Favorites,
  Active-in-game, Single-copy, Unrecognized, Recently-added); **Tags** section + "+" new-tag + tag rows;
  **Collections** section; **Maintenance** tools (Rebuild symlinks, Batch-fix encoding, Find duplicates, Find
  stale — w/ badges); **Dependency-analysis** sub-actions (Scan installed/all/Saves, Analyze VaM log). Each
  filters or navigates. *Test:* `LibraryRailTests` (`Saved_views_carry_counts_and_filter`, `Tag_click_filters`,
  `Maintenance_tool_navigates`).
- [ ] **GD-3 · Library facet bar.** *Read:* `:317–332`. *Add:* swap AutoCompleteBox → `SC-11 SearchableCombo`
  with per-creator **counts**; **Installed** checkbox (fix current "Favorites" mislabel); **Reset**; filter
  **chips** (All types ×, Tier ×, + Filter); "rows x–y of N" position; **Sort dropdown** (Recently used / Size↓
  / Hot→Cold / Most depended-on). *Test:* `LibraryFacetTests` (`Creator_combo_shows_counts_and_filters`,
  `Reset_clears_filters`, `Sort_dropdown_changes_order`).
- [ ] **GD-4 · Library table (full columns).** *Read:* `:335–343`, JS `:683–693`. *Add:* adopt `SC-3 DataTable`;
  row **checkbox**; **6 per-type count** columns (Sc/Lk/Cl/Hr/Pl/Mo); **Tier** col + temp dot; **Copies** col;
  **State** pill (ok/needs-fix/missing); **Fix Var** rebuild link; **Detail** button (opens var-detail via GA-1);
  sort carets on all sortable headers. *Test:* `LibraryTableTests` (`All_columns_render`, `State_pill_per_row`,
  `Detail_button_opens_dialog`, `Checkbox_selects_row`).
- [ ] **GD-5 · Library ops bar (wired).** *Read:* `:345–354`, `17` §B/Library ops. *Add/Wire:* "select all N
  matching"; **Install**, **Uninstall**, **Delete**, **Move to subfolder…**, **Add to preset…**, **Fix
  encoding**, **Export→txt**, **Install from txt** — all bound to BE-G1/dialogs. *Test:* `LibraryOpsTests`
  (each button executes its command; Delete opens confirm dialog).
- [ ] **GD-6 · Library detail panel.** *Read:* `:357–390`. *Add:* hero + letter; tag row (temp/tier/installed/
  favorite); action grid (Uninstall, Favorite, Locate, Open-full-detail→dialog); **Content-previews** section
  (type dropdown + Loadable-only + Hide/Fav + 3×2 strip); **Dependencies** list w/ state pills + "resolve via
  alias→" (opens alias dialog); **Copies** list. Needs BE-G6. *Test:* `LibraryDetailTests`
  (`Tag_row_and_actions_render`, `Dependencies_show_state_pills`, `Resolve_link_opens_alias_dialog`).
- [ ] **GD-7 · Repositories cards.** *Read:* `:395–414`. *Add:* screen-head Review-rebalance + Add-repository;
  per-card spec line (speed/var count), **usage bar**, capacity+reserve line, buttons (Re-benchmark, Tier ▾,
  Edit, Rebalance→dialog); dimmed offline variant. Needs BE-G2. *Test:* `RepositoriesViewTests`
  (`Card_shows_usage_bar_and_capacity`, `Rebenchmark_executes`, `Rebalance_opens_migrate_dialog`).
- [ ] **GD-8 · Presets detail.** *Read:* `:417–440`. *Add:* screen-head Import-txt + New-preset; per-preset
  dots; active tag; Edit(→dialog)/Diff/Export; infobox (deps/aliases/missing + resolve); **member table**
  (Package/Resolution/State pill); Deactivate-all. Needs BE-G3. *Test:* `PresetsViewTests`
  (`Member_table_renders_with_state`, `Switch_and_deactivate_execute`, `Edit_opens_preset_dialog`).
- [ ] **GD-9 · Tiering.** *Read:* `:443–457`. *Add:* screen-head Simulate-policy(→BE-G5) + Review-migration;
  4 tabs (GC-2); bars on the 3 class cards; misplaced table **Size** col + per-row **Plan…** button
  (→migrate dialog). *Test:* `TieringViewTests` (`Class_cards_show_bars`, `Plan_button_opens_migrate_dialog`,
  `Simulate_shows_predicted_plan`).
- [ ] **GD-10 · Duplicates & reclaim.** *Read:* `:460–474`. *Add:* 4 tabs (GC-2); **3 reclaim summary cards**
  (Duplicate copies / Cold-on-SSD / Never-loaded) + their buttons; group table **Locations** + **Reclaim**
  cols; Review→dupe dialog. *Test:* `DupesViewTests` (`Three_reclaim_cards_render`,
  `Review_opens_dupe_dialog`).
- [ ] **GD-11 · Health & fix.** *Read:* `:477–490`. *Add:* screen-head Fix-all; 3 tabs (GC-2); **3 summary
  cards** (GBK/Shift-JIS/Low-confidence) + Fix buttons; detail table (Detected/Broken/Confidence pill/Fix→dialog).
  *Test:* `HealthViewTests` (`Summary_cards_render`, `Fix_opens_fix_dialog`).
- [ ] **GD-12 · Missing deps.** *Read:* `:493–500`. *Add:* screen-head Export-links; **Alias-to-owned** col;
  **Scope** col (global/preset tag); Resolve/Edit-alias → alias dialog (wired). *Test:* `MissingViewTests`
  (`Alias_and_scope_columns_render`, `Resolve_opens_alias_dialog`).
- [ ] **GD-13 · Proposals.** *Read:* `:503–518`. *Add:* screen-head Reject-all + Approve-selected; 5 tabs
  (GC-2); per-card **checkbox** + **icon** + **tag** + **Review…** button (→matching dialog). *Test:*
  `ProposalsViewTests` (`Card_has_checkbox_icon_tag`, `Approve_selected_executes`, `Review_opens_dialog`).
- [ ] **GD-14 · Analytics.** *Read:* `:521–538`. *Add:* **bars** on by-type + by-creator; **Wasting-fast-
  storage** card (StatTile + link→Tiering); **Usage-over-time** sparkline card. *Test:* `AnalyticsViewTests`
  (`Bars_render_on_both_cards`, `Four_cards_present`, `Sparkline_renders`).
- [ ] **GD-15 · Trash & backup.** *Read:* `:541–549`. *Add:* 2 tabs (GC-2 — Trash / Catalog backups[BE-G4]);
  row **checkbox**; summary line; **Restore selected** / **Purge selected** bulk buttons; **Trashed** time col;
  backups-tab list. *Test:* `TrashViewTests` (`Bulk_restore_and_purge_execute`, `Backups_tab_lists_backups`).
- [ ] **GD-16 · Activity history.** *Read:* `:552–560`. *Add:* action-filter dropdown (All/Migrations/Deletes/
  Fixes); tag styling on Action. *Test:* `ActivityViewTests` (`Filter_narrows_the_log`, `Action_renders_as_tag`).
- [ ] **GD-17 · Settings (all tabs).** *Read:* `:563–573`. *Add:* 5 tabs (GC-2 — General/Tiers&policy/
  Automation/Import/Advanced); General: Catalog-DB-location field, **Symlink-type dropdown**, Fix-on-import
  **dropdown** (Flag/Prompt/Auto — fix current textbox), Preset-extraction-defaults row. *Test:*
  `SettingsViewTests` (`Five_tabs_render`, `Fix_on_import_is_a_dropdown`, `Save_persists_all_fields`).

---

## Section G-E — Dialog internals + reachability (one unit per dialog) · fixes `17` §A.1/§C
Each: fill the missing internal controls **and** confirm the dialog opens from its trigger (GA-1). Ticking
requires HR-G1's reachability test.

- [ ] **GE-1 · Onboarding.** *Read:* `:581–589`. *Add:* 4-step **stepper**; benchmarked-folder table.
- [ ] **GE-2 · Add repository.** *Read:* `:591–596`. *Add:* **Browse…**; tier **dropdown**; okbox; reserve
  field; "rebalance existing" checkbox.
- [ ] **GE-3 · Migrate.** *Read:* `:598–607`. *Add:* copy→verify→rename→delete **flow stages**; file table;
  wire **Dry run** + **Approve & run** (BE-G2/migration).
- [ ] **GE-4 · Fix encoding.** *Read:* `:609–617`. *Add:* before/after **mojibake preview**; "also slim"
  checkbox; wire apply-to-group.
- [ ] **GE-5 · Alias.** *Read:* `:619–626`. *Add:* owned-package **search box**; **scope dropdown**; wire save.
- [ ] **GE-6 · Confirm delete.** *Read:* `:628–636`. *Add:* **reverse-dependency** note; wire move-safe→trash.
- [ ] **GE-7 · Preset edit.** *Read:* `:638–645`. *Add:* Add-from-filter / Import buttons; **member table** w/
  version-pin checkboxes (BE-G3).
- [ ] **GE-8 · Dupe review.** *Read:* `:647–654`. *Add:* keep-one **radio** table; wire keep/trash (BE-G1).
- [ ] **GE-9 · Rescue.** *Read:* `:656–660`. *Add:* baseline-preset **dropdown**; wire apply.
- [ ] **GE-10 · Var detail.** *Read:* `:662–666`. *Add:* 4 tabs (Overview/Dependency-graph/Content-items/
  Copies&lineage) with BE-G6 data.

---

## Section G-F — Polish & regression

- [ ] **GF-1 · Toast on real actions.** Wire `ShowToast` (+ Undo) to Delete/Move/Fix/Alias completions. *Test:*
  `ToastWiringTests` (`Delete_shows_undo_toast`, `Undo_reverses`).
- [ ] **GF-2 · Empty/loading/error per screen (HR-7).** Every GD screen shows explicit states from its live
  feed. *Test:* per-screen state tests.
- [ ] **GF-3 · Full-shell E2E over real repos.** Launch → index (`D:\VarVault_test_repo`) → browse → open each
  dialog → run a Move (`E:`→`F:`) → verify trash/undo. *Test:* `ShellE2ETests` (extends POL-4 smoke to prove
  wiring end-to-end, not just navigation).
- [ ] **GF-4 · Checklist reconciliation.** Update `16` DoD wording so a box requires **item fidelity + wired**
  evidence (HR-G0), not skeleton-existence, so this regression cannot recur.

---

### Done-tracking
Tick a box only with a green **wired** test named next to it (HR-G0). Total gap units: **G-A 6 · BE-G 6 ·
G-C 2 · G-D 17 · G-E 10 · G-F 4 = 45 units.** Do G-A first — it converts the static shell into a live app.
