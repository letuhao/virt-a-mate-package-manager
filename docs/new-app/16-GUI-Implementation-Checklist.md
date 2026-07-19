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
- [ ] **SC-12 · JobRow.** *Read:* `:235–237`. Title + cancel × + progress bar + sub-line. *Test:* binds `JobHandle` progress; cancel invokes `JobContext.Cancellation`.

---

## Section 1 — Backend enablement (BE-N)
Each = new SDK interface + Infrastructure impl + DI registration + **integration test that drives the
engine from the SDK boundary** (closes the "engine never runs" gap; see 15-plan §Engine audit). Algorithms
are specified in 15-plan §New backend tasks catalog — read that entry before coding each.

- [ ] **BE-N0 · Index orchestration + trigger.** `IIndexOrchestrator.IndexAllAsync/IndexRepositoryAsync`;
  after index → `ResolveAllAsync` → `IUsageAnalyzer.RecomputeAsync`, as one `IJobQueue` job. **Fix**
  `EfCatalogStore.RefreshOneAsync` `HasMissingDeps=false` clobber. *Test:* index a temp repo via the
  orchestrator → `PackageListItem`s exist, `HasMissingDeps` populated, usage recomputed.
- [ ] **BE-N1 · `IDashboardService`.** *Test:* summary totals/tiers/classes/reclaim/attention from a seeded catalog.
- [ ] **BE-N2 · `ITieringService`** (PlacementPolicy/MigrationPlanner/RebalancePlanner/UsageAnalyzer). *Test:* misplaced detection + plan build with a full-tier.
- [ ] **BE-N3 · `IMigrationService`** (MigrationRunner/DurableFileMover). *Test:* run a plan → copy→verify→rename→delete, source trashed, single-copy excluded.
- [ ] **BE-N4 · `IReclaimService`** (DedupGrouping/DeletionPredicate/Sha256FileHasher). *Test:* exact groups; keep-one trashes only hash-verified redundant copies; single-copy protected.
- [ ] **BE-N5 · `IHealthService`** (EncodingHealthEngine/EncodingFixCoordinator). *Test:* encoding groups; fix writes UTF-8 var, keeps original, validates load.
- [ ] **BE-N6 · `ITrashQueryService`** (ITrashService/SqliteDatabaseBackup). *Test:* list/restore/purge round-trip; backup+restore.
- [ ] **BE-N7 · `IProfileService`** (IVamProfileService). *Test:* list profiles; switch repoints one symlink (skip where privilege absent).
- [ ] **BE-N8 · `IProposalService`** (aggregates N2/N4/N5/stale). *Test:* proposals listed; approve dispatches to the right runner; reject records.
- [ ] **BE-N9 · `IPackageDetailQuery`** (IDependencyGraph fwd+rev, ContentItem, VarFile lineage, IThumbnailStore). *Test:* detail returns deps closure, content items, copies for a seeded package.
- [ ] **BE-N10 · `ILibraryActionService`** (activation/move/preset/txt). *Test:* install creates links; delete gated by `DeletionPredicate` → trash; export/import txt round-trips.
- [ ] **BE-N11 · `IAliasService`** (VarAlias). *Test:* set alias → resolver substitutes it; missing→owned mapping applied on activation.
- [ ] **BE-N12 · Library facets** (`ITagService`/`ICollectionService`; extend `LibraryQuery` with PackageName/InstalledOnly/Types/Tiers; saved views). *Test:* each new filter narrows results; tag add/query.
- [ ] **BE-N13 · Onboarding/add-repo** (Register/Benchmark + reserveBytes + BE-N0). *Test:* register → benchmark → suggested tier → index.
- [ ] **BE-N14 · Command palette / search** (action registry + `LibraryQuery.SearchText`). *Test:* query returns package hits + nav actions.

---

## Section 2 — Shell spine (SH)

- [ ] **SH-1 · ShellViewModel + navigation.** Owns `ActiveScreen` + `NavigateCommand`; hosts the 13 screen
  VMs. *Rule:* screens resolved via DI, lazily shown. *Test:* navigate switches `ActiveScreen`.
- [ ] **SH-2 · Rail nav.** *Read:* `:196–222`. *Items:* Shell rail nav #1–20 (13 nav + 4 group labels + 3
  badges). Uses SC-11. *Test:* 13 items present, grouped Browse/Optimize/Problems/System; badges bind counts.
- [ ] **SH-3 · Top bar.** *Read:* `:225–232`. *Items:* Top-bar #1–6 (search, rescue, jobs, dot, add-repo,
  theme). *Test:* buttons bind commands; jobs dot reflects `IJobQueue.Active`.
- [ ] **SH-4 · Jobs panel.** *Read:* `:233–238`. *Items:* Jobs-panel #7–20 (header, pause-all, 3× JobRow via
  SC-12). *Test:* rows bind live `IJobQueue.Active` with progress + cancel.
- [ ] **SH-5 · Log dock.** *Read:* `:576`. *Items:* Log-dock #1–3 (index status, selected count, tier %). *Test:* binds job status + BE-N1 tier %.
- [ ] **SH-6 · AppHost wiring.** Replace stub: build host, register **all** screen VMs + shell, resolve
  `ShellViewModel`. *Rule:* no orphaned VMs. *Test:* host resolves shell with all 13 screens non-null.

---

## Section 3 — Screens (SCR)
Each cites its plan item numbers (15-plan) and its HTML range. Read both before coding (HR-0).

- [ ] **SCR-1 · Dashboard.** *Read:* `:241–286`. *Items:* Dashboard #1–40. *BE:* N1, N2, N4, N8, N5, `IActivityLog`, `IRepositoryService`. *Test:* 6 cards render seeded summary; attention links navigate.
- [ ] **SCR-2 · Library.** *Read:* `:288–392` + JS `:673–704`. *Items:* Library #1–78. *BE:* `ILibraryQueryService`, N9, N10, N11, N12, N5. Uses SC-3/SC-8. *Test:* rail filters, facets, table+gallery, ops-bar, detail panel each bind (stub SDK); reuse existing `MainWindowUiTests` + extend.
  - [ ] SCR-2a Rail (#1–18) · [ ] SCR-2b Facet bar (#19–29) · [ ] SCR-2c Table cols (#30–45) · [ ] SCR-2d Gallery (#46–53) · [ ] SCR-2e Ops bar (#54–63) · [ ] SCR-2f Detail panel (#64–78). *(Sub-units each independently testable.)*
- [ ] **SCR-3 · Repositories.** *Read:* `:395–414`. *Items:* Repos #1–42. *BE:* `IRepositoryService`, N2, N13. *Test:* 4 repo cards bind capacity/tier/online; actions invoke Benchmark/SetEnabled.
- [ ] **SCR-4 · Loading presets.** *Read:* `:417–440`. *Items:* Presets #1–16. *BE:* `IPresetService`, N7, N10, N11. *Test:* list + member table bind; Switch invokes N7.
- [ ] **SCR-5 · Tiering & migration.** *Read:* `:443–457`. *Items:* Tiering #1–23. *BE:* N1, N2, N3. *Test:* class counts + misplaced table; Plan opens m-migrate.
- [ ] **SCR-6 · Duplicates & reclaim.** *Read:* `:460–474`. *Items:* Dupes #1–22. *BE:* N4, N2. *Test:* reclaim cards + exact-dup groups bind; Review opens m-dupe.
- [ ] **SCR-7 · Analytics.** *Read:* `:521–538`. *Items:* Analytics #1–15. *BE:* `IAnalyticsService`, N2, `IUsageAnalyzer`. *Test:* space-by-type/creator bars + usage chart bind.
- [ ] **SCR-8 · Proposals & review.** *Read:* `:503–518`. *Items:* Proposals #1–39. *BE:* N8. *Test:* tabs + 4 proposal cards; Approve dispatches, Reject records.
- [ ] **SCR-9 · Health & fix.** *Read:* `:477–490`. *Items:* Health #1–19. *BE:* N5. *Test:* tabs + group cards + detected table; Fix opens m-fix.
- [ ] **SCR-10 · Missing deps.** *Read:* `:493–500`. *Items:* Missing #1–8. *BE:* `IMissingDepsQuery`, N11. *Test:* table binds; Resolve opens m-alias.
- [ ] **SCR-11 · Trash & backup.** *Read:* `:541–549`. *Items:* Trash #1–12. *BE:* N6. *Test:* trash table restore/purge; backups tab.
- [ ] **SCR-12 · Activity history.** *Read:* `:552–560`. *Items:* History #1–5. *BE:* `IActivityLog`. *Test:* audit table binds; kind filter narrows.
- [ ] **SCR-13 · Settings.** *Read:* `:563–573`. *Items:* Settings #1–11. *BE:* `ISettingsService`. *Test:* tabs + fields load/save via settings.

---

## Section 4 — Dialogs / modals (DLG)
Each is content inside SC-9. Read its exact modal lines (HR-0).

- [ ] **DLG-1 · m-onboard.** *Read:* `:581–589`. *Items:* Modal onboard #1–10. *BE:* N13, N0. *Test:* stepper + folder table; Continue indexes.
- [ ] **DLG-2 · m-addrepo.** *Read:* `:591–596`. *Items:* addrepo #1–8. *BE:* Register/Benchmark, N2. *Test:* detected media/tier bind; Add&index registers.
- [ ] **DLG-3 · m-migrate.** *Read:* `:598–607`. *Items:* migrate #1–14. *BE:* N3. *Rule:* show copy→verify→rename→delete flow + single-copy exclusion warning. *Test:* plan summary + Approve runs job (stub).
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
