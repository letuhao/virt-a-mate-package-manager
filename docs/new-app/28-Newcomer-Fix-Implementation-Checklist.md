# 28 — Newcomer Fix Implementation Checklist

Evidence-gated remediation for the findings in [27-Newcomer-Real-Run-Findings.md](27-Newcomer-Real-Run-Findings.md).
Same rules as docs 24/26: a box is `[x]` **only** with concrete evidence (a passing test name, a screenshot, or run
output). Test through SDK interfaces / real view-models, not internals. Every real-data test stays env-var-gated
(`VARVAULT_TEST_CORPUS` / `_2` / `VARVAULT_VAM_PATH`) — no hardcoded paths. Baseline before starting: `dotnet build` 0
errors, `dotnet test` green, `NewcomerRealRunE2ETests` passes.

Priority order: **Phase A** (quick, high-impact polish) → **B** (landing + empty states) → **C** (onboarding) →
**D** (missing-deps triage) → **E** (tiering hint) → **F** (verify/polish). Phases A/B/F are low-risk; C/D are
larger UX changes — land each behind its own test.

---

## Phase A — Readability quick wins

### A1 · Shared byte humanizer (fixes **D2**)
Root cause: byte→GB/TB formatting is duplicated ad-hoc in 5 view-models
([AnalyticsViewModel.cs:15](../../src/VarVault.App/ViewModels/AnalyticsViewModel.cs#L15),
[GalleryCardViewModel.cs:30](../../src/VarVault.App/ViewModels/GalleryCardViewModel.cs#L30),
[LibraryViewModel.cs:711](../../src/VarVault.App/ViewModels/LibraryViewModel.cs#L711),
[ProposalsViewModel.cs:27](../../src/VarVault.App/ViewModels/ProposalsViewModel.cs#L27),
[RepositoriesViewModel.cs:34](../../src/VarVault.App/ViewModels/RepositoriesViewModel.cs#L34)) and **absent** where
Dashboard/Dupes/Library-grid bind raw `long` bytes.

- [x] **A1.1** Add `VarVault.Common.Formatting.ByteSize.Humanize(long bytes)` → `"1.55 TB"` / `"487.7 GB"` / `"812 MB"`
  / `"64 KB"` / `"0 B"`, `InvariantCulture`, thresholds `1<<40 / 1<<30 / 1<<20 / 1<<10`. Unit tests: `0`, `1023`,
  `1<<20`, `1_500_000_000`, `2_000_000_000_000`.
  *Evidence:* `ByteSizeTests` (new, `Category=Unit`).
- [x] **A1.2** Add `VarVault.App.Controls.BytesToSizeConverter : IValueConverter` wrapping `ByteSize.Humanize`
  (null/non-numeric → `""`). Register in [App.axaml](../../src/VarVault.App/App.axaml) resources as `BytesToSize`.
- [x] **A1.3** Apply the converter at the four raw-byte bindings:
  - [DashboardView.axaml:30](../../src/VarVault.App/Views/DashboardView.axaml#L30) — `CapacityBytes`
  - [DashboardView.axaml:72](../../src/VarVault.App/Views/DashboardView.axaml#L72) — `ReclaimableBytes` (drop `'{0} bytes'`)
  - [DupesView.axaml:12](../../src/VarVault.App/Views/DupesView.axaml#L12) + `:23` — `ReclaimableBytes`
  - [LibraryView.axaml](../../src/VarVault.App/Views/LibraryView.axaml) library-grid **Size** cell — `TotalSize`
- [x] **A1.4** (cleanup, optional) Refactor the 5 ad-hoc VM helpers to call `ByteSize.Humanize` (delete duplication).
  *Evidence:* `Grep` shows no remaining `/ (double)(1L << 30)` in `src/VarVault.App/ViewModels`.
- [x] **A1.5** Acceptance: extend `NewcomerRealRunE2ETests` to assert the Dashboard/Dupes size labels contain a unit
  (`GB`/`TB`/`MB`) and no bare digit-only byte string; add a screenshot diff note.
  *Evidence:* `NewcomerRealRunE2ETests` assertion + `newcomer-dashboard.png` / `newcomer-dupes.png` show "GB/TB".

### A2 · Dashboard "missing dependencies" label (fixes **D3**)
Dashboard tile = `Summary.MissingDepsCount` = `PackageListItems.Count(HasMissingDeps)` (**165**, *your* packages);
Missing screen/badge = distinct absent refs (**1328**). Same words, ~8× different number.

- [x] **A2.1** Relabel the tile so it can't be read as the Missing-deps total. Change
  [DashboardView.axaml:61](../../src/VarVault.App/Views/DashboardView.axaml#L61) StringFormat to
  `'{}{0} packages need dependencies →'` (keep navigation to `missing`).
- [x] **A2.2** (decision) Confirm we keep two distinct metrics (packages-with-gaps vs absent-refs). If instead the
  tile should show the absent-ref count to match the badge, source it from `IMissingDepsQuery` in
  [EfDashboardService.cs](../../src/VarVault.Infrastructure/Library/EfDashboardService.cs) — **do not** silently leave
  two numbers under one label.
  *Evidence:* `DashboardScreenTests` asserts the tile text; `NewcomerRealRunE2ETests` records both numbers with
  distinct labels and flags no collision.

### A3 · Repository display name (fixes **M1**)
[AddRepoViewModel.cs:41](../../src/VarVault.App/ViewModels/AddRepoViewModel.cs#L41) hardcodes the name `"repository"`,
so every repo card reads "repository" ([RepositoriesView.axaml:27](../../src/VarVault.App/Views/RepositoriesView.axaml#L27)).

- [x] **A3.1** Derive the name from the folder: `Path.GetFileName(FolderPath.TrimEnd(sep))` (fallback `"repository"`
  when empty/root). Pass it to `RegisterRepositoryRequest(name, path)`.
- [x] **A3.2** Same fix on the onboarding add-drives path if it registers repos independently
  ([OnboardingViewModel.cs](../../src/VarVault.App/ViewModels/OnboardingViewModel.cs) / `IOnboardingService`).
  *Evidence:* `NewcomerRealRunE2ETests` asserts the two repo cards show `VarVault_test_repo` and
  `VarVault_test_repo_01`, not "repository".

---

## Phase B — Landing + empty states

### B1 · Land on Dashboard, not Library (fixes **U3**)
- [x] **B1.1** Change `AppHost.CreateShell(… initial: "library")` →
  [`"dashboard"`](../../src/VarVault.App/Composition/AppHost.cs#L61). Verify the dashboard auto-loads via
  `ILoadableScreen` on first nav (it does — `DashboardViewModel : ILoadableScreen`).
  *Evidence:* `AppHostShellTests` asserts `shell.ActiveScreenId == "dashboard"` at startup.

### B2 · Empty-state guidance (fixes **U2**)
- [x] **B2.1** Activity history: [ActivityView.axaml](../../src/VarVault.App/Views/ActivityView.axaml) — add a
  `TextBlock` "No activity yet — actions you take will appear here." bound `IsVisible="{Binding IsEmpty}"` (add
  `IsEmpty` to [ActivityViewModel.cs](../../src/VarVault.App/ViewModels/ActivityViewModel.cs) if absent).
- [x] **B2.2** Presets: [PresetsView.axaml](../../src/VarVault.App/Views/PresetsView.axaml) — in the empty main area
  add "No loading presets yet." + a "+ New preset" CTA, bound to the existing
  [`PresetsViewModel.IsEmpty`](../../src/VarVault.App/ViewModels/PresetsViewModel.cs#L43).
  *Evidence:* `UiE2E` render tests (`Category=E2E`) find the empty-state text on both screens for an empty catalog;
  `newcomer-history.png` / `newcomer-presets.png` show guidance instead of a blank card.

---

## Phase C — First-run onboarding (fixes **D1**)
Today [App.axaml.cs](../../src/VarVault.App/App.axaml.cs) opens straight to `MainWindow`; the built
`OnboardingViewModel` only fires from the Dashboard "Setup wizard" button. A zero-repo newcomer gets no guidance.

- [x] **C1.1** Add `bool needsOnboarding` to composition: true when `IRepositoryService.ListAsync()` returns empty at
  startup. Compute it in [AppHost.TryCreateShellOrError](../../src/VarVault.App/Composition/AppHost.cs#L167) (own
  scope; don't share the shell DbContext) and expose on the shell (e.g. `ShellViewModel.ShowOnboardingOnLoad`).
- [x] **C1.2** In [MainWindow.axaml.cs `OnLoaded`](../../src/VarVault.App/Views/MainWindow.axaml.cs#L19), if
  `ShowOnboardingOnLoad`, open the onboarding dialog once via the existing
  [`DialogLauncher.OpenOnboarding`](../../src/VarVault.App/Services/DialogLauncher.cs#L45) path (wire a shell hook like
  `AddRepoHandler`). Guard so it shows at most once per launch.
- [x] **C1.3** Persist a "seen onboarding" flag via `ISettingsService` so returning users with repos aren't nagged
  (belt-and-suspenders alongside the repo-count check).
  *Evidence:* new `FirstRunOnboardingTests` — (a) empty catalog ⇒ `ShowOnboardingOnLoad == true`; (b) after a repo is
  registered ⇒ `false`. `NewcomerRealRunE2ETests` records that a fresh install surfaces onboarding before Step 1.

---

## Phase D — Missing-deps triage (fixes **U1**)
1328 rows, per-row "Resolve" only, no bulk path ([MissingDepsViewModel.cs](../../src/VarVault.App/ViewModels/MissingDepsViewModel.cs),
[MissingView.axaml](../../src/VarVault.App/Views/MissingView.axaml)).

- [x] **D1.1** Add a summary header line: "N packages referenced but not in your library, needed by M of your packages."
  (`Items.Count` + distinct dependents), so the number has context.
- [x] **D1.2** Cap the initial render (e.g. top 200 by needed-by) with a "show all N" toggle — the 1.5 s load is the
  slowest screen; virtualization or a cap keeps it snappy. `log()`/label the cap so it doesn't read as "only 200 exist".
- [x] **D1.3** Add a bulk action: "Export all links txt" already exists; add "Copy Hub search list" or a bulk
  select→resolve. Minimum viable: make clear (subtitle/tooltip) these are **downloads you still need**, not fixable
  in-app, so the newcomer isn't hunting for a non-existent auto-fix.
  *Evidence:* `DupesHealthMissingE2ETests` (or a new `MissingTriageTests`) asserts the header count + cap toggle;
  `newcomer-missing.png` shows the contextual header. Re-measure load ms in `newcomer-real-run.md` (< 500 ms target).

---

## Phase E — Tiering context for HDD-only users (fixes **U4**)
Both repos auto-detected T3 (HDD) ⇒ Hot 0 / Warm 0 / Cold 274; the tiering value prop is inert and unexplained.

- [x] **E1.1** When no T1/T2 repository exists, show an inline hint on
  [Tiering](../../src/VarVault.App/Views/TieringView.axaml) and/or the Dashboard classification card: "All storage is
  cold (HDD). Add or tag a fast drive (SSD/NVMe) as T1/T2 to benefit from tiering." Source the condition from the repo
  list tiers (already loaded).
  *Evidence:* `TieringE2ETests` asserts the hint appears when all repos are T3 and disappears when a T1 repo is added.

---

## Phase F — Verify / polish

- [x] **F1** (M2) Library facet-bar overlap — **root cause was a non-responsive layout**, not just width: 8 controls
  were crammed into one fixed 8-column `Grid`, so a narrow window crushed the `*` Search box into the packageName box.
  **Fixed** by converting the facet bar to a `WrapPanel` with fixed/min widths (Search 240/min180, packageName 140) so
  the filters **wrap to the next row** instead of overlapping ([LibraryView.axaml:55-72](../../src/VarVault.App/Views/LibraryView.axaml#L55-L72)).
  *Evidence:* `newcomer-library.png` (1280px) shows a clean single row, no overlap; `newcomer-library-narrow.png`
  (1040px, captured by `NewcomerRealRunE2ETests` after resizing the live window) shows the bar wrapping gracefully onto
  3 rows. Full solution green (608 tests).
  *Follow-up (out of scope for M2):* at extreme widths (&lt;800px) the Library's fixed tri-column shell (tags 180 /
  content / detail 280) still crushes the table — a broader responsive redesign, tracked separately.
- [x] **F2** (M3) Make "+ Add repository" consistent: top-bar/Repos open the Add-Repo dialog; Dashboard "quick action"
  currently binds `SetupWizardCommand` ([DashboardView.axaml:101-102](../../src/VarVault.App/Views/DashboardView.axaml#L101-L102)).
  Decide one behavior (recommend: quick action → Add-Repo dialog; keep "Setup wizard" separate). *Evidence:* `TopBarWiringTests`.
- [x] **F3** Full regression: `dotnet build` 0 errors, `dotnet test` green (App + Infrastructure + E2E + Architecture),
  and re-run `NewcomerRealRunE2ETests` end-to-end over D:/E:/F: — the report's "Dead-ends" section must be empty for
  A–B–C findings. *Evidence:* run output + regenerated `newcomer-real-run.md`.

---

## Traceability

| Finding | Items | Risk |
|---|---|---|
| D1 no onboarding | C1.1–C1.3 | med |
| D2 raw bytes | A1.1–A1.5 | low |
| D3 missing label | A2.1–A2.2 | low |
| U1 missing triage | D1.1–D1.3 | med |
| U2 empty states | B2.1–B2.2 | low |
| U3 landing screen | B1.1 | low |
| U4 tiering hint | E1.1 | low |
| M1 repo name | A3.1–A3.2 | low |
| M2 facet overlap | F1 | low |
| M3 add-repo consistency | F2 | low |
