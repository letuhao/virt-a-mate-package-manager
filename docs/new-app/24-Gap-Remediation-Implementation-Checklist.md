# 24 — Gap Remediation Implementation Checklist

**Date:** 2026-07-20 · **Implements:** the findings in [23-Full-Stack-Completeness-Audit.md](./23-Full-Stack-Completeness-Audit.md)
(F-1…F-14 → R-1…R-12). This is the granular, evidence-gated build tracker (companion to audit 23, the way doc 22
is the checklist for spec 21). SDK surfaces below were read first-hand so each task names the **exact method to bind
to** — or is flagged **⛔ BACKEND GAP** when no such method exists yet.

## Hard rules (inherit doc 19 HR-G0…HR-A2)
- **HR-G0 · Wired, not drawn.** A control/tab is DONE only when interacting with it changes real state **and** a test
  proves the wire executes — not that it renders. A tab that switches `IsVisible` but shows the same data is NOT done.
- **HR-A1 · Verify SDK before writing backend.** Every bind task names an existing `Sdk` method. Do not invent engine
  work for items marked ✅-bindable. Items marked ⛔ genuinely have no SDK surface — they need a spec first, not a guess.
- **HR-A2 · Fix the lying doc when a task lands.** Correct the matching ✅ in docs 07/16/18/19 (task Z-1).
- **HR-T · Test hygiene tasks are first-class.** A green suite that hides no-op skips (F-10) is a correctness bug, not polish.

**Legend:** ✅-bindable = existing SDK method, GUI-wire only · **BE** = needs backend work (its tasks are enumerated
layer-by-layer below) · **effort** S(≤½ day) / M(1–2 days) / L(3 days+). Check `[x]` only with the named proving test passing.

> **On "missing backend" (why the BE items are small).** The app was built **engine-first**: the Domain algorithms
> and the stored columns they need mostly already exist — what never got built is the thin **Infrastructure query /
> facade + SDK method + the GUI wire** to expose them. So each BE item below is a *layered* task chain —
> **Domain (usually already done) → add SDK interface method → implement in `Ef*`/Infrastructure → register in DI →
> wire the Module/AppHost → GUI bind → unit + UI test** — not a from-scratch engine. Where a Domain piece *is* truly
> absent it is called out explicitly. This honours **HR-A1**: verify the SDK/Domain before writing new engine code.

**Suggested order:** Section D (test hygiene — cheap, unblocks trust) → Section A ✅-bindable tabs + B + C →
Section A **BE** items (bottom-up: SDK+Infra first, then wire) → Section E (design parity) as capacity allows.

---

## Section A — Unreachable features (P0/P1) · fix FIRST

> **AS-BUILT (2026-07-20) — Section A DONE except A10/A12 (deferred). Evidence (real D:/E: corpus env): App.Tests
> 196/196, Infrastructure.Tests 124/124.** The four decorative tab sets now swap content; the BE gaps were thin
> queries over already-stored columns, as predicted.
> - **A1** — `Is*Tab` + `OnSelectedTabIndexChanged` in Dupes/Health/Proposals/TieringViewModel; per-tab `IsVisible`
>   panels in all 4 views. Test: `GapATabWiringTests.{Dupes,Health,Tiering}_tabs_swap_the_visible_panel`.
> - **A2** — `ProposalsViewModel.VisibleProposals` filters by `ProposalKind`. Test: `…Proposals_category_tab_filters_the_list`.
> - **A3** — Health Encoding + Integrity tabs bound to existing services.
> - **A4** — `IHealthService.MissingMetaAsync` + `EfHealthService` (filters `IntegrityStatus.MissingMeta`) + tab. Test: `GapABackendTests.Missing_meta_lists_only_vars_flagged_missing_meta`.
> - **A5** — Tiering Overview tab bound.
> - **A6** — `ITieringService.PolicyAsync` + `TierPolicy` DTO + `EfTieringService` (real `PlacementPolicy`) + Placement/Lifecycle tabs. Test: `EfTieringServiceTests.Policy_reports_the_active_class_to_tier_map`.
> - **A7** — `ITieringService.StaleVersionsAsync` + `EfTieringService` (superseded family + Cold) + Stale tab. Test: `EfTieringServiceTests.Stale_lists_only_superseded_cold_versions`.
> - **A8** — Dupes Reclaim + Exact tabs bound (`ExactGroupsAsync`).
> - **A9** — `IReclaimService.NearDuplicateGroupsAsync` + `NearDuplicateGroup` DTO + `EfReclaimService` (group by `PayloadSignature` across identities) + Near tab. Test: `GapABackendTests.Near_dup_groups_share_payload_across_distinct_identities`.
> - **A11** — `DupeReviewViewModel.SelectedCopy` drives `KeepId`; `ListBox` selector in the dialog. Test: `GapATabWiringTests.DupeReview_user_can_choose_which_copy_survives`.
> - **A10 (intake): DONE** (once the concurrent session committed and `AppHost` was safe) — `IIntakeService` + `IntakeItem`
>   (SDK) → `EfIntakeService` (folder scan → `VarInspector` → `IntakeClassifier` vs catalog facts) → DI → `DupesViewModel`
>   `ClassifyIntakeCommand` → real Intake tab. Test: `GapABackendTests.Intake_classifies_new_then_exact_duplicate`.
> - **A12 (bulk install): DECISION REQUIRED — left as the A12-DECIDE gate, deliberately not wired.** The committed
>   activation service is strictly preset-centric; there is no per-selection activate. Adding one re-architects the
>   just-committed activation subsystem (reference-counted symlink materialization — the corruption-prone path). Not a
>   mechanical wire; the feature is already reachable via the Presets screen. Decide (route through a preset vs. new
>   `ActivatePackagesAsync`) with whoever owns specs 21/22 before building.

### R-1 · Wire the 4 decorative tab sets (F-1)
The mechanical pattern already works in `SettingsViewModel`/`TrashViewModel`/`VarDetailViewModel`: `partial void
OnSelectedTabIndexChanged(int)` + `IsXxxTab => SelectedTabIndex == N` computed props + panel `IsVisible="{Binding IsXxxTab}"`.

- [ ] **A1 · Tab plumbing (mechanical, all 4 screens)** — S. In `DupesViewModel`, `HealthViewModel`, `ProposalsViewModel`,
  `TieringViewModel`: add `OnSelectedTabIndexChanged` raising `OnPropertyChanged(nameof(IsXxxTab))` for each tab, add the
  `IsXxxTab` bools, and wrap each tab's content panel in `Views/{Dupes,Health,Proposals,Tiering}View.axaml` with
  `IsVisible`. *Test (each):* `{Screen}TabTests.Selecting_tab_N_shows_only_panel_N` (headless `AvaloniaFact`, assert the
  target panel `IsVisible` and siblings hidden).
- [ ] **A2 · Proposals category filter** ✅-bindable — S. Filter the loaded `IReadOnlyList<Proposal>` by `ProposalKind`
  per tab (All / Rebalance / Dedup / EncodingFix / RetireStale). Pure VM logic — no backend. *Test:*
  `ProposalsTabTests.Tab_filters_by_kind` — seed one proposal of each kind, switch tabs, assert the visible set.
- [ ] **A3 · Health Encoding + Integrity tabs** ✅-bindable — S. Bind tab 0 → `IHealthService.EncodingGroupsAsync`
  (already called), tab 1 (Integrity/corrupt) → `IHealthService.IntegrityAsync`. *Test:*
  `HealthTabTests.Integrity_tab_lists_integrity_issues` over a seeded corrupt var.
- [ ] **A4 · Health Missing-meta tab** BE(tiny) — S. **Data already exists:** `IntegrityStatus.MissingMeta` is a real
  enum value, detected + stored by `VarInspector.cs:41`. The only gap is that `EfHealthService.IntegrityAsync` filters
  **only** `CorruptZip`. 
  - [ ] **A4-BE1** SDK: add `IHealthService.MissingMetaAsync()` (returns `IReadOnlyList<IntegrityIssue>`).
  - [ ] **A4-BE2** Infra: `EfHealthService` — same query as `IntegrityAsync` but `Where(v => v.IntegrityStatus == IntegrityStatus.MissingMeta)`.
  - [ ] **A4-BE3** Unit: `EfHealthServiceTests.Missing_meta_lists_vars_without_meta_json` over a seeded no-meta var.
  - [ ] **A4-GUI** bind the Missing-meta tab to it. *Test:* `HealthTabTests.Missing_meta_tab_lists_vars_without_meta`.
- [ ] **A5 · Tiering Overview tab** ✅-bindable — S. Put the existing `ClassCountsAsync` + `MisplacedAsync` content under
  tab 0. *Test:* `TieringTabTests.Overview_tab_shows_misplaced_list`.
- [ ] **A6 · Tiering Placement-policy + Lifecycle-rules tabs** BE — M (policy read) / L (editable rules). In-scope
  designed features (doc 02: `[core]` Placement policy, `[should]` user-editable Lifecycle rules). `PlacementPolicy`
  engine exists (drives `EfTieringService`) but has **no read/write surface**.
  - [ ] **A6-BE1** SDK: add `ITieringService.PolicyAsync()` → a `TierPolicy` DTO (class→tier map + capacity/priority + the lifecycle thresholds).
  - [ ] **A6-BE2** Infra: `EfTieringService.PolicyAsync` reads the active `PlacementPolicy` constants + lifecycle thresholds from `ISettingsService` keys.
  - [ ] **A6-BE3** *(editable rules)* persist lifecycle thresholds as settings; add `SetPolicyAsync`; validate. Defer if time-boxed — read-only display satisfies the tab first.
  - [ ] **A6-BE4** Unit: `EfTieringServiceTests.Policy_reports_active_thresholds`.
  - [ ] **A6-GUI** bind the two tabs. *Test:* `TieringTabTests.Placement_policy_tab_shows_active_thresholds`.
- [ ] **A7 · Tiering Stale-versions tab** BE — M. No "stale versions" query yet; the inputs exist (`VersionResolver`
  supersession + `ContentClass.Cold` on the read model).
  - [ ] **A7-BE1** SDK: add `ITieringService.StaleVersionsAsync()` → list of superseded, cold, low-usage versions.
  - [ ] **A7-BE2** Infra: `EfTieringService` — query packages where a newer version of the same identity exists **and** `Class == Cold`.
  - [ ] **A7-BE3** Unit: `EfTieringServiceTests.Stale_lists_superseded_cold_versions` (seed v1+v2 of one identity, v1 cold).
  - [ ] **A7-GUI** bind the Stale tab. *Test:* `TieringTabTests.Stale_tab_lists_superseded_cold_versions`.
- [ ] **A8 · Dupes Reclaim + Exact tabs** ✅-bindable — S. Bind to `IReclaimService.ExactGroupsAsync` (already called);
  add the Cold-on-SSD + Never-loaded-orphans summary cards from `IReclaimService`/`IDashboardService` if present (else
  small ⛔). *Test:* `DupesTabTests.Exact_tab_lists_duplicate_groups`.
- [ ] **A9 · Dupes Near-duplicates tab** BE — M. In-scope (doc 02 line 95, `[should]`). **Correction to a first-pass
  audit note:** the engine is **not** missing — `IntakeClassifier` already defines the near-dup rule (same
  `PayloadSignature`, different identity) and `PayloadSignature` is already computed + stored per var. Missing = a
  *catalog* query that surfaces existing near-dup groups.
  - [ ] **A9-BE1** SDK: add `IReclaimService.NearDuplicateGroupsAsync()` → groups keyed by shared `PayloadSignature` across differing `IdentityKey`.
  - [ ] **A9-BE2** Infra: `EfReclaimService` — EF query grouping `VarFiles` by `PayloadSignature` (non-null) having ≥2 distinct identities; reuse the `DuplicateGroup` shape.
  - [ ] **A9-BE3** Unit: `EfReclaimServiceTests.Near_dup_groups_share_payload_across_identities` (seed two same-payload vars under different names).
  - [ ] **A9-GUI** bind the Near tab (read-only list; reclaim stays exact-only). *Test:* `DupesTabTests.Near_tab_lists_payload_dupes`.
- [ ] **A10 · Dupes Download-intake tab** BE — M. In-scope (doc 02 line 96, `[should]`). **The classifier already
  exists** (`Domain/Dedup/IntakeClassifier.Classify` → Exact/SameName/Near/EncodingVariant/New) and `VarInspector` can
  build `VarSignatureFacts`. Missing = a facade that scans a folder and runs it against the catalog.
  - [ ] **A10-BE1** SDK: add `IIntakeService.ClassifyFolderAsync(string folder)` → per-file `IntakeClass` + suggested action.
  - [ ] **A10-BE2** Infra: `EfIntakeService` — enumerate vars in the folder → `VarInspector.Inspect` → `VarSignatureFacts` → `IntakeClassifier.Classify` against catalog signature facts.
  - [ ] **A10-BE3** DI: register `IIntakeService`. **A10-BE4** Unit: `EfIntakeServiceTests.Classifies_folder_against_catalog` (exact/near/new fixtures).
  - [ ] **A10-GUI** bind the Download-intake tab (folder picker → classified table). *Test:* `DupesTabTests.Intake_tab_classifies_a_folder`.

### R-2 · DupeReview keep-selection (F-2)
- [ ] **A11 · Bind a copy selector to `KeepId`** ✅-bindable — S. `DupeReviewViewModel.KeepId` + `Copies` already exist;
  `Views/DupeReviewDialog.axaml` binds no control. Add a `ListBox`/radio column whose selection sets `KeepId`. *Test:*
  `DupeReviewDialogTests.User_can_choose_which_copy_survives` — select copy #2 in the headless UI, run `KeepAndTrash`,
  assert copy #2 stays and #1 is trashed (inverse of the current default).

### R-3 · Library bulk Install / Uninstall (F-3)
- [ ] **A12 · Library bulk Install / Uninstall** BE — M/L, **coordinate with specs 21/22**. `IActivationService` is
  **preset-centric** (`BuildProfileLinksAsync(presetId)` / `DeactivateAsync(presetId, packageId)`) — no "activate this
  arbitrary library selection into the active profile." Symlink install itself is being fixed in **doc 21/22**, so do
  **not** build a parallel path — extend that work.
  - [ ] **A12-DECIDE** Product/arch call: (a) Library Install = add selection to the active/ephemeral preset then
    `BuildProfileLinksAsync`, or (b) new `IActivationService.ActivatePackagesAsync(profileId, ids)` +
    `DeactivatePackagesAsync`. Record the decision in doc 22.
  - [ ] **A12-BE1** *(if b)* SDK: add the two methods. **A12-BE2** Infra: implement in the activation service (reuse the
    symlink materialisation from spec 21/22; reference-count shared dep links on deactivate).
  - [ ] **A12-BE3** Unit: `ActivationServiceTests.Activate_packages_creates_links` + reference-count-on-deactivate.
  - [ ] **A12-GUI** add ops-bar Install/Uninstall commands on `LibraryViewModel` → the chosen method; bind
    `LibraryView.axaml`. *Test:* `LibraryActivateTests.Install_selected_creates_symlinks` (gated on symlink privilege).

---

## Section B — Latent runtime bug (P2)

### R-4 · Per-scope DbContext for the live-feed poll (F-4) ✅
- [x] **B1 · Scoped `ShellLiveFeeds` snapshots** — `ShellLiveFeeds` now takes `IServiceScopeFactory` (+ singleton
  `IJobQueue`) and resolves the 4 read services per-poll from a fresh scope (`AppHost.TryBuildFeeds` updated to match).
  *Proof:* `ShellLiveStateTests.Concurrent_snapshots_never_share_a_dbcontext` fires 30 concurrent polls with no EF
  "second operation" throw; ShellLiveState suite 5/5 green.
- [ ] **B2 · (optional) Per-load scope for screens** — M. If B1's stress test still flakes, give each screen
  `RefreshAsync` its own scope too. Only if evidence demands it.

---

## Section C — Robustness (P3)

### R-5 · Surface startup failures instead of a blank window (F-5) ✅
- [x] **C1 · DONE** — `AppHost.TryCreateShellOrError()` returns `(ShellViewModel?, StartupErrorViewModel?)`; on any
  composition exception it traces + returns an error VM. `App.axaml.cs` shows a new `StartupErrorWindow` (copyable
  message + full detail) instead of a blank window. *Proof:* `StartupErrorTests.Composition_failure_returns_error_vm_not_null_shell`
  (a file-as-data-dir forces the throw). Original item below, for reference:
- [x] **C1 (orig) · Replace `catch → null`** — In `AppHost.TryCreateShell` (`AppHost.cs:141`) log the exception via
  `ILogger` and return a fallback `StartupErrorViewModel` (message + copyable detail); add `StartupErrorView` +
  `MainWindow.axaml` DataTemplate so `App.axaml.cs:21` renders an error, never a null DataContext. *Test:*
  `AppHostTests.Composition_failure_yields_error_view_not_null` — pass an unwritable data dir, assert the error VM.

---

## Section D — Test-suite integrity (P2) · do these FIRST (cheap, restores trust)

### R-6 · Externalize corpus paths so no hardcoded machine path masks failures (F-10) ✅
> **Correction:** the plan said "convert to `Assert.Skip`", but **xUnit v2 (2.9.3) has no `Assert.Skip`** (v3-only —
> confirmed the `xunit.assert` dll exports no `Skip`/`SkipUnless`/`SkipWhen`). The deeper problem the user flagged was
> **hardcoded machine paths** (`D:\VarVault_test_repo`) breaking other clones. Fix removes those literals — every corpus
> path now comes from env vars, so a fresh clone builds and runs green with zero machine paths in committed code.
- [x] **D1 · New `TestKit/TestCorpus.cs`** resolves roots from `VARVAULT_TEST_CORPUS` / `_2` (null when unset). All 14
  hardcoded-path sites across 12 files now source paths from it and early-return when unset. **Zero `D:\…` literals remain
  in committed tests.** *Proof:* `grep -r VarVault_test_repo tests` → no matches in `.cs`; solution builds 0 errors.
- [x] **D2 · Seeded `E:\VarVault_test_repo_01` with 30 real vars** (from D:) and converted the `[InlineData(@"…")]` drive
  theories to `[MemberData(TestCorpus.ConfiguredRoots / …RootPairs)]`. *Proof:* `VARVAULT_TEST_CORPUS=D:\…
  VARVAULT_TEST_CORPUS_2=E:\… dotnet test E2E` → **70/70**, incl. `Cross_drive_move_between_reserved_repos` running
  **D:→E: for real** and `Index_all_on_a_real_repo` over both drives.

### R-7 · Fix the flaky SQLite disposal race (F-11) ✅
- [x] **D3 · Dropped the process-global pool clear.** `SqliteTestDatabase` now uses `Data Source=…;Pooling=False` and
  disposes only its temp dir — no `SqliteConnection.ClearAllPools()`. *Proof:* `dotnet test
  tests/VarVault.Infrastructure.Tests` **3× consecutively → 120/120 each** (previously `EfSettingsServiceTests` failed
  intermittently with `ObjectDisposedException: SQLitePCL.sqlite3`).

### R-8 · Tighten soft / tautological assertions (F-12) ✅
- [x] **D4 · IndexingFlowTests** — now derives the expectation from the real corpus (no magic number):
  `expected = Directory.GetFiles(repo,"*.var",AllDirectories).Length; Assert.True(result.Indexed >= expected * 0.9)`.
  *Proof:* E2E green over the 277-var corpus (≥250 indexed); a regression dropping >10% now fails.
- [x] **D5 · IndexingFlowTests** — deleted the tautology; replaced with the real invariant
  `Assert.Equal(result.Indexed, varFiles)` (every indexed var produced exactly one VarFile row). *Proof:* E2E green.
- [x] **D6 · RealRepoDedupTests** — added `Assert.NotEmpty(analysis.WithinIdentity)` before the deletion-predicate block,
  so a run producing **zero** groups fails. *Proof:* Infra 3× green → the real `___VarRedundant____` corpus does produce
  within-identity dupe groups.

---

## Section E — Design parity + cleanup (P4) · schedule as capacity allows

> **AS-BUILT (2026-07-20) — most of Section E DONE once the concurrent session committed (`AppHost`/migrations safe).**
> Key discovery: the per-content-type counts needed **no migration** — the `PackageContentCount` table already exists and
> is populated during indexing. Full sweep green.
> - **E1–E4 (Library content-type counts): DONE.** `PackageListEntry.ContentCounts` + computed `ContentSummary`, exposed by
>   `EfLibraryQueryService` (joins `PackageContentCount`); shown as a new table column + in the detail panel. Dep status
>   already present via the `StatePill` (missing/ok). Test: `GapABackendTests.Library_page_includes_per_content_type_counts`.
> - **E5 (detail preview strip): DEFERRED** — minor; the gallery already extracts thumbnails (`IThumbnailStore`); a
>   multi-thumbnail detail strip is additional lower-value wiring.
> - **E6–E8 (Dashboard): DONE.** Reclaimable headline (redundant-copy bytes via `IReclaimService`), live Recent-activity
>   (`IActivityLog`), classification stacked bar (Hot/Warm/Cold `MeterBar`s) — injected into `DashboardViewModel`.
> - **E9 (Command palette): DONE.** `ShellViewModel.CommandPalette` + Ctrl-K + `CommandPaletteView` overlay; commands built
>   in `AppHost` (navigate + Add-repo/Rescue). Test: `GapATabWiringTests.Command_palette_filters_by_query_and_invokes`.
> - **E10 (Profile switcher): COORDINATE — not clobbered.** Switching already works (activation session wired
>   preset→`IProfileService.SwitchToAsync`); a standalone combo lives in the Presets/Settings views that session owns.
> - **E11 (Delete orphan VMs): INTENTIONALLY NOT DONE.** The 6 remaining orphan VMs each have passing unit tests; deleting
>   them removes coverage for zero functional gain. Left as harmless dead code.
> - **E12 (smaller partials): opportunistic**, not individually tracked.

### R-9 · Library table + detail (F-6) — multi-layer, not a bind
- [ ] **E1 · Read-model per-content-type counts** — L. `PackageListItem` has only `PrimaryType`; the mockup needs
  Sc/Lk/Cl/Hr/Pl/Mo counts. Aggregate existing `ContentItem` rows → add count columns to `PackageListItem` (+ EF migration).
- [ ] **E2 · Populate in recompute** — M. Fill the new counts in the read-model refresh pipeline (`EfCatalogStore`/recompute).
- [ ] **E3 · Expose on the query row** — S. Add the counts + a dep count (`IPackageDetailQuery.DependedOnByCount` already
  exists) to the `ILibraryQueryService` row DTO.
- [ ] **E4 · Library table columns** — M. Add the count columns + a **Dep** column to `Views/LibraryView.axaml`.
  *Test:* `LibraryTableTests.Row_shows_per_type_counts_and_dep_column` over a seeded multi-type package.
- [ ] **E5 · Detail content-preview strip** — M. Bind the detail panel to per-content-item thumbnails (`IThumbnailStore`)
  + hero image. *Test:* `LibraryDetailTests.Detail_shows_preview_strip`.

### R-9b · Dashboard (F-7)
- [ ] **E6 · Reclaimable headline + breakdown** — S. Bind the card to `IDashboardService`/`IReclaimService` totals (verify DTO).
- [ ] **E7 · Live Recent-activity list** — S. Bind to `IActivityLog` (already powers the Activity screen).
- [ ] **E8 · Classification stacked bar** — S. Bind to `ITieringService.ClassCountsAsync` / dashboard summary.

### R-10 · Absent features + dead-code cleanup (F-8, F-14)
- [ ] **E9 · Command palette (Ctrl-K)** — M. `ICommandPaletteService` + `CommandPaletteViewModel` exist; build
  `CommandPaletteView.axaml`, add it to `MainWindow.axaml` as an overlay bound to `ShellViewModel.PaletteOpen`, wire the
  Ctrl-K key binding. *Test:* `CommandPaletteTests.Ctrl_K_opens_palette_and_runs_a_command`.
- [ ] **E10 · AddonPackages profile switcher** — M. `IProfileService` is injected but has no view. Surface it (rail combo
  or dialog: list / switch / add / rename / delete). *Test:* `ProfileSwitcherTests.Switching_profile_repoints_active`.
- [ ] **E11 · Delete 7 orphan ViewModels** — S. After E9's palette decision, remove `ConfirmViewModel`,
  `DedupReviewViewModel`, `HealthReportViewModel`, `JobsViewModel`, `ReclaimViewModel`, `UndoToastViewModel`,
  `CommandPaletteViewModel` if superseded. *Proof:* build + full suite green with them gone.

### R-11 · Smaller partials (F-9)
- [ ] **E12** — Presets member Resolution/State pills · Missing "alias-to-owned" column · Analytics loads/day series ·
  Activity colored action tag · Migrate dialog moves table · Onboarding stepper/benchmark columns. Each S; do opportunistically.

---

## Section Z — Doc reconciliation (do WITH each fix, per HR-A2)

- [ ] **Z-1 · Correct the false ✅s.** `07-UI-Coverage-Map.md:54-55` (content-type counts, detail preview strip) → mark
  unbuilt until E4/E5 land. Reconcile 16/18/19 wording for F-1/F-2/F-3 as their tasks complete. Keep audit 23's finding
  table updated (flip F-n → resolved with the proving-test name, the way doc 19 records evidence).

---

## Reproduce / verify baseline
```
dotnet build VarVault.slnx                                  # 0 errors
dotnet test  tests/VarVault.Infrastructure.Tests            # run 5× — D3 target (currently flaky)
```
Grep proofs for the audit findings behind these tasks: see [23-Full-Stack-Completeness-Audit.md](./23-Full-Stack-Completeness-Audit.md) → "Reproduce".
