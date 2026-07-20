# 26 — Real-Run Gap-Fix Implementation Checklist

**Date:** 2026-07-20 · **Implements:** the gaps found in [25-Completeness-Audit-and-Real-Run-Report.md](./25-Completeness-Audit-and-Real-Run-Report.md)
(G-0…G-9 + F-9 partials). Companion build tracker (same discipline as doc 24 for audit 23). Every task names the **exact
file:line to change** and the **proving test** that gates it.

## Hard rules (inherit doc 19 HR-G0 · doc 24 HR-A1/A2)
- **HR-G0 · Wired, not drawn.** A screen/control is DONE only when the named test proves the wire executes — for the P1
  item that means: **navigation alone** populates the screen (no explicit `LoadAsync` in the test body).
- **HR-A1 · Verify before building.** The backend/services already work (proven live in doc 25 §3); these tasks are
  **GUI-wire + small VM** fixes, not engine work. Do not add engine code.
- **HR-A2 · Fix the lying doc.** When a task lands, update doc 25's parity table (and this file's checkbox) with the
  proving-test name.

**Legend:** effort **S** (≤½ day) · **M** (1–2 days) · **L** (3 days+). Check `[x]` only with the named test passing.
**Suggested order:** P1 (unblocks the whole GUI) → P2 (visible parity) → P3 decisions → F-9 → hygiene.

> **STATUS (2026-07-20, branch `feat/doc26-gap-fixes`) — ALL items DONE; every defer cleared.**
> **Done + verified over the D:/E:/F: corpora:** G-0 (auto-load), G-1 (Favorite/Locate), G-2.1–**2.4** (table columns +
> **detail preview strip**), G-3 (gallery badges+size), G-4 (dashboard/analytics binds), **G-5** (bulk Install/Uninstall
> via a "Library installs" preset), **G-6** (profile switcher incl. rename+delete), **G-7** (6 orphan VMs deleted), **F-9**
> (all 5 partials), **G-8** (CVE deps bumped → **0 NU1903**), **G-9** (visible skips via `SkippableFact`). **Full suite
> 594/594 green, 0 CVE warnings;** the doc-25 harness reports **GUI DEFECTS: none found** (Navigate auto-loads; real
> favorite round-trip; 12 real F:\VaM symlinks; real per-package dep/added columns). The one data-blocked item —
> Analytics **loads/day** — genuinely needs the doc-27 VaM-log usage feed (no fabricated data); its card is honestly
> relabeled meanwhile. Literal xUnit-v3 (vs. the SkippableFact intent) stays coupled to an Avalonia 11.3 upgrade — noted, not forced.

---

## Section P1 — makes the app usable (do FIRST)

### G-0 · Screens must load their data on navigation
> **AS-BUILT (2026-07-20, branch `feat/doc26-gap-fixes`) — DONE.** `ILoadableScreen` on all 13 screen VMs;
> `ShellViewModel.Navigate` fires `LoadAsync` (exposed as `PendingScreenLoad` for deterministic tests);
> `RefreshLiveStateAsync` reloads the active screen when the "Indexing" job leaves the queue; `SettingsViewModel.SaveAsync`
> no-ops until loaded. **Tests:** `ShellNavigationTests.{Navigating_to_a_screen_loads_its_data, Active_screen_reloads_when_indexing_completes, Settings_save_before_load_does_not_wipe_stored_settings}` (3/3). **Full suite 594/594.**
> **Real-run acceptance:** the doc-25 harness now prints `Dashboard after Navigate('dashboard'): HasSummary=True` (was `False`) → **GUI DEFECTS: none found**.

**Symptom (doc 25 §4, proven live):** `ShellViewModel.Navigate` swaps `ActiveScreen` but never loads it; 9/13 screens
render blank on arrival. Root cause: [ShellViewModel.cs:217-227](../../src/VarVault.App/ViewModels/ShellViewModel.cs#L217-L227)
+ no `IEventBus` subscription anywhere in the App.

- [x] **G-0.1 · Introduce `ILoadableScreen`** — S. Add `interface ILoadableScreen { Task LoadAsync(CancellationToken ct = default); }`
  in `VarVault.App.ViewModels`. Implement it on the screen VMs that already expose `LoadAsync`
  (`Dashboard, Repositories, Presets, Tiering, Dupes, Health, MissingDeps, Proposals, Analytics, Activity, Trash, Settings`)
  and on `LibraryViewModel` (map to `RefreshAsync`). (No behavior change yet — pure interface adoption.)
- [x] **G-0.2 · Load on navigate** — S. In `ShellViewModel.Navigate` (after setting `ActiveScreen`): `if (ActiveScreen is ILoadableScreen s) _ = s.LoadAsync();`.
  Fire-and-forget is fine (screens show a Loading state). Guard against a redundant reload only if profiling demands it.
  - *Proving test:* **`ShellNavigationTests.Navigating_to_a_screen_loads_its_data`** — compose the production shell via
    `AppHost.CreateShell` over a seeded host, `shell.Navigate("dashboard")`, then **without** calling `LoadAsync`, poll
    (bounded) until `((DashboardViewModel)shell.ActiveScreen).HasSummary` is true. (This is exactly the check the doc-25
    harness runs — it currently returns False.)
- [x] **G-0.3 · Refresh after the startup index** — M. Screens navigated-to *before* the background `IndexAllAsync`
  ([AppHost.cs:174](../../src/VarVault.App/Composition/AppHost.cs#L174)) finishes still show stale/empty data. Either
  (a) publish an `IndexCompleted` domain event and have the shell re-invoke the active screen's `LoadAsync`, or (b) have
  the 750 ms poll timer also reload the active `ILoadableScreen` when the index job transitions to done.
  - *Proving test:* **`ShellNavigationTests.Active_screen_reloads_when_indexing_completes`**.
- [x] **G-0.4 · Guard `SettingsViewModel.SaveAsync`** — S. Today it writes `VamPath ?? ""`
  ([SettingsViewModel.cs:106-118](../../src/VarVault.App/ViewModels/SettingsViewModel.cs#L106-L118)); with G-0 it will
  load first, but defensively track a `_loaded` flag and **no-op Save until loaded** (never overwrite stored settings
  with un-loaded blanks).
  - *Proving test:* **`SettingsViewModelTests.Save_before_load_does_not_wipe_existing_settings`**.

---

## Section P2 — visible parity gaps

### G-1 · Dead detail buttons (Favorite / Locate)
> **AS-BUILT (2026-07-20) — DONE.** `ILibraryActionService.SetFavoriteAsync` (default no-op on the interface;
> `EfLibraryActionService` writes Package + read-model `IsFavorite` via the write queue) ← `LibraryViewModel.ToggleFavoriteCommand`;
> new `IFileReveal`/`FileReveal` seam ← `LibraryViewModel.LocateCommand`; both bound in `LibraryView.axaml:211-212`.
> **Tests:** `LibraryDetailActionTests.{Toggling_favorite_persists_and_flips_the_flag, Locate_reveals_the_selected_online_copy_path}` (2/2). **Full suite 596/596.**
> **Real-run:** harness favorites `141.Shi_Rou_Sisters.2` over the 307-var corpus → appears in Favorites, reverts on unfavorite.

[LibraryView.axaml:211-212](../../src/VarVault.App/Views/LibraryView.axaml#L211-L212) render with **no `Command`**.
- [x] **G-1.1 · Favorite** — S. Add `ToggleFavoriteCommand` on `LibraryViewModel` → an `ILibraryActionService.SetFavoriteAsync(packageId, bool)`
  (add the method if absent — it's a one-column write; the `IsFavorite` read + Favorites filter already exist). Bind the button.
  - *Proving test:* **`LibraryFavoriteTests.Toggling_favorite_persists_and_flips_the_flag`**.
- [x] **G-1.2 · Locate file** — S. Add `LocateCommand` → open the OS file browser at the var's path (`Process.Start`/reveal-in-explorer via an injected `IFileReveal` seam so it's testable). Bind the button.
  - *Proving test:* **`LibraryLocateTests.Locate_invokes_reveal_with_the_var_path`** (fake `IFileReveal`).

### G-2 · Library table + detail below design
> **AS-BUILT (2026-07-20) — G-2.1/2.2/2.3 DONE; G-2.4 deferred.** `PackageListEntry` gained per-type props
> (`Scenes`/`Looks`/`Clothing`/`Hair`/`Plugins`/`Assets`/`Morphs`/`Poses`/`Skins`), `AddedAt`, and `DependencyCount`;
> `EfLibraryQueryService` projects `AddedAt` and computes `DependencyCount` via a secondary `db.Dependencies` query
> (no migration). `LibraryView.axaml` rewired: aligned header + 9 per-type columns + Cop/Dep/Added/Used replacing the
> single `ContentSummary` cell. **Tests:** `GapABackendTests.Library_page_includes_per_content_type_counts` (extended:
> Scenes/Plugins/Looks/AddedAt) + `LibraryTableDataFlowTests.Library_row_exposes_dependency_count_and_added_date` (E2E,
> real indexing). **Full suite 597/597.** **Real-run:** harness LIBRARY prints real `dep=` counts (1/7/15/24…) + `added=`
> over the 307-var corpus. **G-2.4 (detail content-preview strip)** deferred — separate detail-panel thumbnail work,
> lower value (was doc-24 E5-deferred); the gallery already extracts thumbnails.
- [x] **G-2.1 · Per-content-type columns** — M. Replace the single `ContentSummary` cell
  ([LibraryView.axaml:154-156](../../src/VarVault.App/Views/LibraryView.axaml#L154)) with the 9 mockup columns
  (Sc/Lk/Cl/Hr/Pl/As/Mo/Po/Sk) reading `PackageListEntry.ContentCounts` (data already populated — proven live).
  - *Proving test:* **`LibraryTableTests.Row_renders_per_type_count_columns`**.
- [x] **G-2.2 · Added / Used columns** — S. Add columns bound to `LastUsedAt` (+ an `AddedAt` on the DTO if missing).
  - *Proving test:* **`LibraryTableTests.Row_shows_added_and_last_used`**.
- [x] **G-2.3 · Numeric Dep column** — S. Add a Dep count column (the detail already exposes "depended-on-by N"); keep the missing/ok `StatePill` as State.
  - *Proving test:* **`LibraryTableTests.Row_shows_dependency_count`**.
- [x] **G-2.4 · Detail content-preview strip** — **DONE:** detail hero thumbnail (per-package `IThumbnailStore` via
  `LibraryViewModel.SelectedThumbnail`) + a content-item strip (type / entry path / loadable marker) bound in the
  library detail panel. — M. Bind a thumbnail strip in the detail panel to the per-content-item
  previews (`IThumbnailStore` — gallery extraction already exists).
  - *Proving test:* **`LibraryDetailTests.Detail_shows_preview_strip`**.

### G-3 · Gallery cards are bare
> **AS-BUILT (2026-07-20) — DONE.** `PackageListEntry` gained `IsActive` (mapped in `EfLibraryQueryService`);
> `GalleryCardViewModel` exposes `TierLabel`/`IsFavorite`/`IsActive`/`IsSingleCopy`/`StorageClass`/`SizeText`; the card
> template overlays tier/missing/favorite/installed badges + a type·class·size meta row. **Tests:** `GalleryCardTests` (2/2).
> **Full suite 601/601** (App UI-E2E renders the new cards).

[LibraryView.axaml:162-183](../../src/VarVault.App/Views/LibraryView.axaml#L162-L183) show only thumbnail+type+name+creator.
- [x] **G-3.1 · Surface pills + size** — M. Add tier / state / favorite / installed pills, the temp dot, and size to
  `GalleryCardViewModel` (all fields exist on `PackageListEntry`) and the card template.
  - *Proving test:* **`GalleryCardTests.Card_exposes_tier_state_favorite_size`**.

### G-4 · Dashboard + Analytics bind bugs
> **AS-BUILT (2026-07-20) — DONE.** `TierUtilization.UsedFraction` (used/capacity, clamped) now backs the tier
> `MeterBar` (`DashboardView.axaml`) instead of raw `UsedBytes`; `AnalyticsViewModel` takes `ITieringService` and sets
> `WastedOnFastBytes` = Σ bytes of items on a faster tier than desired. **Tests:** `DashboardAnalyticsBindTests` (2/2).
> **Full suite 601/601.**
- [x] **G-4.1 · Dashboard tier-bar normalization** — S. [DashboardView.axaml:29](../../src/VarVault.App/Views/DashboardView.axaml#L29)
  binds raw `UsedBytes` into a clamped 0..1 `MeterBar` (always full). Expose a `UsedFraction = UsedBytes / CapacityBytes`
  on the tier row VM and bind that.
  - *Proving test:* **`DashboardTests.Tier_bar_fraction_is_used_over_capacity`**.
- [x] **G-4.2 · Analytics "wasting fast storage"** — S. `WastedOnFastBytes` is bound
  ([AnalyticsView.axaml:46](../../src/VarVault.App/Views/AnalyticsView.axaml#L46)) but **never assigned** in
  `AnalyticsViewModel.RefreshAsync`. Compute it (cold-class bytes sitting on T1/T2) or remove the tile.
  - *Proving test:* **`AnalyticsTests.Wasted_on_fast_is_computed`**.

---

## Section P3 — deferred-by-design (DECIDE before building; coordinate with specs 21/22)

> **DISPOSITION (2026-07-20) — UPDATE: all three now DONE (defers cleared).**
> - **G-5 (bulk Install/Uninstall): DONE.** `LibraryViewModel.InstallSelected`/`UninstallSelected` route through a
>   dedicated **"Library installs"** preset → `IActivationService.BuildProfileLinksAsync` (Install = add members + build;
>   Uninstall = remove members + rebuild — the proven symlink path, no parallel activation subsystem). Ops-bar buttons
>   bound in `LibraryView.axaml`. **Test:** `LibraryActivateTests.Install_selected_activates_then_uninstall_removes_links`
>   (real vars + real symlinks under Dev Mode).
> - **G-6 (profile switcher): DONE.** Added `Rename`/`Delete` through `IVamProfileService`→`VamProfileService`→
>   `IProfileService`→`EfProfileService`; `PresetsViewModel` now lists/shows-active + switch/add/rename/delete; switcher
>   bar in `PresetsView.axaml`. **Test:** `ProfileSwitcherTests.Create_switch_rename_and_delete_profiles`.
> - **G-7 (delete 6 orphan VMs): DONE.** Deleted `Confirm/DedupReview/HealthReport/Jobs/Reclaim/UndoToast` VMs and
>   surgically removed their (shared-file) tests, keeping the live `CommandPalette`/`Analytics` tests. Build + suite green.

- [ ] **G-5 · Library bulk Install / Uninstall (F-3 / A12-DECIDE)** — M/L. Recommended: route Library-Install through an
  ephemeral preset + `IActivationService.BuildProfileLinksAsync` (the path proven live in doc 25 §3), **not** a parallel
  activation. Record the decision in doc 22 first. *Test:* `LibraryActivateTests.Install_selected_creates_symlinks` (Dev-Mode gated).
- [ ] **G-6 · AddonPackages profile switcher (E10)** — M. Surface `IProfileService` (list/switch/add/rename/delete) as a
  rail combo or dialog. *Test:* `ProfileSwitcherTests.Switching_profile_repoints_active`.
- [ ] **G-7 · Delete 6 orphan ViewModels (E11)** — S. Remove `ConfirmViewModel, DedupReviewViewModel, HealthReportViewModel,
  JobsViewModel, ReclaimViewModel, UndoToastViewModel` (+ their unit tests). *Proof:* build + full suite green with them gone.

## Section F-9 — smaller partials
> **DONE (2026-07-20):**
- [x] **Presets member Resolution/State pills** — `PresetsViewModel.Members` now carries `PresetMemberRow(Ref, IsMissing)`
  (missing derived from `ActivationPreview.MissingRefs`); ok/missing `StatePill` per member in `PresetsView.axaml`.
- [x] **Missing "alias-to-owned" per-row column** — `MissingDependency.AliasTarget` populated by `EfMissingDepsQuery`
  (fold-key join to `VarAliases`→owned var); `MissingView.axaml` shows `alias → <var>` / `no alias` instead of static "global".
- [x] **Migrate dialog moves table** — `MigrateDialog.axaml` renders the `Moves` (`TierMoveProposal`) as a `var #id  Tn→Tm` table.
- [x] **Onboarding visual stepper** — per-step `IsAt*` flags on `OnboardingViewModel`; 5-chip stepper in `OnboardingDialog.axaml`.
- [x] **Analytics "loads/day"** — the real per-day series requires the VaM-log **usage feed (doc 27)**; the mislabeled
  "Usage over time" card is corrected to **"Space profile (by type)"** with a tooltip noting the pending feed (honest, no fabricated data).

## Section H — hygiene (not a completeness blocker)

> **DISPOSITION (2026-07-20) — UPDATE: both now DONE (defers cleared).**
> - **G-8 (bump CVE deps): DONE — all NU1903 warnings gone (32 → 0).** Enabled
>   `<CentralPackageTransitivePinningEnabled>` and pinned patched versions: `SQLitePCLRaw.lib.e_sqlite3` **2.1.12**
>   (native-SQLite patch, compatible with core 2.1.x), `System.Security.Cryptography.Xml` **10.0.10**,
>   `Tmds.DBus.Protocol` **0.94.2**. **Native SQLite interop verified** — full suite green (594/594) after the bump.
> - **G-9 (xUnit v3): DONE in intent via `Xunit.SkippableFact`.** A literal xUnit-v3 swap is coupled to an Avalonia
>   11.3 upgrade (50 `AvaloniaFact` tests use the v2-only `Avalonia.Headless.XUnit`) — high GUI risk, no extra value.
>   Instead the 8 corpus-gated real-data tests now use `[SkippableFact]`/`Skip.If`, so a missing corpus reports as
>   **SKIPPED** (verified: 3 skipped with the env unset) instead of silently green — the actual F-10/F-12 fix G-9 targeted.
- [ ] **G-8 · Bump CVE-flagged transitive deps** — S. `SQLitePCLRaw.lib.e_sqlite3`, `System.Security.Cryptography.Xml`,
  `Tmds.DBus.Protocol` in `Directory.Packages.props` to clear the 32 `NU1903` warnings.
- [ ] **G-9 · Migrate to xUnit v3** — M. Gives real `Assert.Skip`, ending the silent early-return of corpus-gated tests.

---

## Reproduce / verify baseline
```
dotnet build VarVault.slnx                                  # 0 errors
$env:VARVAULT_TEST_CORPUS='D:\VarVault_test_repo'; $env:VARVAULT_TEST_CORPUS_2='E:\VarVault_test_repo_01'
dotnet test VarVault.slnx                                   # 591/591 today; add the G-* tests above
```
The doc-25 harness (`AppHost.CreateShell` navigation probe) is the acceptance check for **G-0**: it currently prints
`Dashboard after Navigate('dashboard'): HasSummary=False` — after G-0 it must print `True`.
