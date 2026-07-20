# 27 — Newcomer Real-Run Findings (UI/UX audit)

**Date:** 2026-07-20 · **Persona:** brand-new user managing two real repositories on different drives + a real VaM game folder.
**Method:** `NewcomerRealRunE2ETests.Newcomer_manages_two_repos_and_a_game_folder_end_to_end` — a fresh install (empty
catalog) that drives the **real** Add-Repo dialog, Settings, rail navigation (auto-load), and preset activation over the
live corpora, screenshotting every screen. Env-var driven (no hardcoded paths):

```
VARVAULT_TEST_CORPUS   = D:\VarVault_test_repo        (repo #1)
VARVAULT_TEST_CORPUS_2 = E:\VarVault_test_repo_01     (repo #2)
VARVAULT_VAM_PATH      = F:\VaM_1.20.77.9             (game folder)
```

Screenshots: `docs/new-app/ui-evidence/newcomer-*.png`. Metrics: `docs/new-app/ui-evidence/newcomer-real-run.md` (regenerated each run).

## Per-screen metrics (this run)

| Screen | Load ms | Primary count | Notes |
|---|---:|---:|---|
| Dashboard | 81 | 274 pkgs | raw byte values (see D2) |
| Library | 124 | 274 | Size column raw bytes; dense but works |
| Presets | 32 | 0 | blank main area, no CTA (U2) |
| Tiering | 29 | balanced | good empty state |
| Duplicates | 18 | 31 groups | raw bytes in subtitle (D2) |
| Analytics | 98 | 10 rows | works |
| Proposals | 181 | 33 | best-designed screen |
| Health | 39 | 4 GBK | CJK detection works |
| **Missing deps** | **1569** | **1328** | slowest; overwhelming (U1) |
| Repositories | 240 | 2 | GB/TB humanized ✅ |
| Trash | 17 | 0 | ok empty state |
| Activity history | 9 | 0 | **blank void** (U2) |
| Settings | 41 | — | ok |

**Headline job succeeded:** created a 5-member preset → **8 symlinks materialized into `F:\VaM`**, 0 missing (Developer Mode on).

## Dead-ends / defects

- **D1 · No first-run onboarding.** `App.axaml.cs` opens straight to `MainWindow`; the shell's initial screen is
  **Library** (`AppHost.CreateShell(initial: "library")`). The onboarding wizard (`OnboardingViewModel`) is fully built
  but only reachable via the Dashboard "Setup wizard" button. A true newcomer (zero repos) lands on an empty Library with
  no proactive "add a repo / set your VaM path" guidance. → Auto-open onboarding when `IRepositoryService` has no repos,
  and/or land on Dashboard.
- **D2 · Raw byte values (unreadable) on Dashboard, Duplicates, Library.** `DashboardView` binds `CapacityBytes` (→
  `3000461418496`) and `ReclaimableBytes, StringFormat='{0} bytes'` (→ `4101826139 bytes`); Duplicates shows
  `4101826139 bytes reclaimable`; the Library Size column shows raw bytes. **Repositories** already humanizes (487.7 GB /
  931.4 GB, 1.55 TB) — a formatter exists; it's just not applied to these bindings. → Route all size bindings through the
  same GB/TB converter.
- **D3 · "Missing dependencies" is two different metrics.** Dashboard "Needs your attention" = **165** =
  `PackageListItems.Count(HasMissingDeps)` (*your* packages that have a gap). The Missing-deps screen title + rail badge =
  **1328** = distinct absent referenced packages. Clicking the dashboard's "165 missing dependencies →" lands on a screen
  showing 1328 — same words, ~8× different number. → Relabel the dashboard tile (e.g. "165 packages need dependencies") or
  reconcile the metric.

## UX friction / won't-scale

- **U1 · Missing-deps screen: 1328 rows, resolve one-at-a-time.** Per-row "Resolve" (alias dialog) + "no alias"; no bulk
  resolve, no Hub-download path. Only realistic escape is "Export links txt". Overwhelming for a newcomer.
- **U2 · Empty-state voids.** Activity history renders a titled but **completely blank** "Audit log" card (no "No activity
  yet"). Presets shows the profile card then a large empty area with no "create your first preset" CTA. (Contrast the good
  empty states on Tiering / Trash / Proposals.)
- **U3 · App opens on Library, not Dashboard.** Dense 274-row table (or empty pre-index) is a disorienting landing.
- **U4 · HDD-only ⇒ everything Cold.** Both repos auto-detected T3 (HDD) → Hot 0 / Warm 0 / Cold 274. The tiered-storage
  value proposition is inert and unexplained for users without a fast drive. → Hint when no T1/T2 drive exists.

## Minor / polish

- **M1** · Both repos display the generic name **"repository"** — `AddRepoViewModel` hardcodes
  `RegisterRepositoryRequest("repository", path)`. → Derive the name from the folder.
- **M2** · Possible overlapping placeholder in the Library facet bar (PackageName search shows "Search" + "packageName…"
  overlapping) — verify in the real windowed app; may be a headless-render artifact.
- **M3** · "+ Add repository" appears in 3 places with 2 behaviors (top-bar/Repos → Add-Repo dialog; Dashboard quick
  action/"Setup wizard" → onboarding).

## What genuinely works (positive)

- Both real repos registered + indexed (274 vars), correct tier/media detection, GB/TB sizes on Repositories.
- **Activation into the real game folder works** — preset → 8 symlinks in `F:\VaM`, 0 missing.
- Dedup found 31 exact groups; Proposals produced 33 verified removal proposals (approve/reject, tabs).
- CJK encoding detection found 4 GBK groups with fix actions.
- All 10 dialogs reachable (dupe-review, migrate, fix-encoding, alias, add-repo, rescue…).
- Fast screen loads (<250 ms except Missing at 1.5 s for 1328 rows); live badges + status bar populate.
