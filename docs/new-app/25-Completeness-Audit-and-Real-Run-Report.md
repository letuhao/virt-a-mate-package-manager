# 25 — Completeness Audit & Real-Run Report

**Date:** 2026-07-20 · **Author:** automated ground-truth audit (doc-independent).
**Scope:** the whole stack (`src/**` + `tests/**`) measured against the **draft HTML mockups** — the sealed design base
(`docs/new-app/mockups/main-window.html`, `docs/new-app/mockups/prototype.html`) — plus a **live real-data run** of the
production composition over real repositories and a real VaM install.

**Method.** (1) Built the solution. (2) Ran the full test suite **twice** with the real corpora wired in via environment
variables. (3) Re-verified every doc-23 finding (F-1…F-14) at HEAD with first-hand `file:line` evidence. (4) Read both
mockups end-to-end and mapped every design element to its implementation, applying **HR-G0 "wired, not drawn"**.
(5) Wrote an isolated harness that drives the **production** composition (`Bootstrap.BuildApp`) over
`D:\VarVault_test_repo` + `E:\VarVault_test_repo_01`, drove the **production shell** (`AppHost.CreateShell`), and
materialized **real symlinks** into the real `F:\VaM_1.20.77.9` game folder. Checklist docs were treated as suspect.

> **Reproducibility / no hardcoded paths.** Every path in the test run and the harness comes from environment variables
> (`VARVAULT_TEST_CORPUS`, `VARVAULT_TEST_CORPUS_2`, `VARVAULT_VAM_ROOT`) — **zero** machine-path literals. A fresh clone
> builds and runs green with the vars unset. The harness lives outside the repo tree and left **`git status` clean**.

---

## TL;DR verdict

| Layer | Reality | Basis |
|---|---|---|
| **Build** | **0 errors** (32 warnings — all `NU1903` transitive-dependency CVE advisories + 1 `NU1510`). | `dotnet build` |
| **Test suite** | **591 / 591 green, twice** (0 failed, 0 skipped). Flakiness (doc-23 F-11) is **fixed**. | `dotnet test` ×2 |
| **Backend / domain / pipeline** | **Real & complete — verified on real data.** All 6 load-bearing algorithms present; **zero** stubs/`TODO` in `src`; every pipeline stage passed live. | real run + grep |
| **GUI structural wiring** | **≈ 80%.** 13 screens + 10 dialogs exist, bound to real services; the doc-23/24 remediation genuinely landed (dead tabs, palette, intake, dashboard). | mockup-vs-XAML |
| **GUI as actually experienced** | **≈ 55–60%. 🔴 One critical defect: screens don't auto-load on navigation** — ~9–11 of 13 render **blank on arrival**. Proven live. | shell-drive run |

**Plain answer to "does everything work?"**
- **The engine: yes.** Driven directly over real data, every backend stage works — indexing 307 real vars across two
  drives, 31 real content-signature duplicate groups, real encoding/corruption detection, real tiering, and **12 real,
  resolvable symlinks** created in a real VaM install and cleaned up.
- **The GUI: mostly wired, but not usable as-is on first navigation.** The screens are correctly bound to those working
  services, but **navigating to a screen never triggers its data load**, so Dashboard, Repositories, Presets, Tiering,
  Dupes, Health, Missing, Proposals, Analytics, History and Settings appear **empty** until an explicit load that the UI
  never issues. This is a small, systemic fix (call `LoadAsync` on navigate/attach) but it is the single most impactful
  gap and it is **not** caught by the green test suite (the UI tests call `LoadAsync` themselves).

Everything else is design-parity polish or deliberately-gated features, enumerated below.

---

## 1 · Environment & how to reproduce

```
# Windows 11, .NET 10 (global.json pins 10.0.302). Developer Mode ENABLED (symlinks need it or elevation).
dotnet build VarVault.slnx                          # 0 errors

$env:VARVAULT_TEST_CORPUS   = 'D:\VarVault_test_repo'        # 277 vars
$env:VARVAULT_TEST_CORPUS_2 = 'E:\VarVault_test_repo_01'     # 30 vars
dotnet test  VarVault.slnx                          # 591/591 — twice; F-11 was intermittent, now stable

# Live harness (drives production Bootstrap.BuildApp + AppHost.CreateShell; all paths from env vars):
$env:VARVAULT_VAM_ROOT = 'F:\VaM_1.20.77.9'
dotnet run --project <harness>                      # index D:+E:, dedup/health/tiering, shell-nav check, symlink-activate F:
```

**On "run as admin".** Creating NTFS symlinks needs **either** an elevated process **or** Windows **Developer Mode**.
Developer Mode was enabled for this run (`HKLM\…\AppModelUnlock\AllowDevelopmentWithoutDevLicense = 1`), after which the
app's `File/Directory.CreateSymbolicLink` path ([SymlinkService.cs:26,41](../../src/VarVault.Infrastructure/Activation/SymlinkService.cs#L26))
succeeds **without** elevation — no per-run UAC. (Windows PowerShell 5.1's `New-Item -SymbolicLink` does *not* pass the
unprivileged-create flag and will still fail; the app and pwsh 7 use the .NET API, which does — a probing gotcha, not an
app limitation.)

## 2 · Build & test evidence

**Build:** `dotnet build VarVault.slnx -c Debug` → **0 Error(s)**, 32 Warning(s) (all `NU1903` — known CVEs in pinned
transitive `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 / `System.Security.Cryptography.Xml` 9.0.0 / `Tmds.DBus.Protocol`; plus
one `NU1510`). Supply-chain advisories, not code defects — worth a dependency bump, out of scope for completeness.

**Full suite, two consecutive runs (real corpora wired in):**

| Assembly | Run 1 | Run 2 |
|---|---|---|
| Architecture | 4 / 4 | 4 / 4 |
| Common | 184 / 184 | 184 / 184 |
| Host | 8 / 8 | 8 / 8 |
| Infrastructure | 126 / 126 | 126 / 126 |
| E2E (incl. real D:+E: indexing & cross-drive move) | 70 / 70 | 70 / 70 |
| App | 199 / 199 | 199 / 199 |
| **Total** | **591 / 591** | **591 / 591** |

Both runs **0 failed, 0 skipped, exit 0.** Doc 23 saw 108-vs-107 flakiness (F-11); **two identical green runs confirm it
fixed** (`SqliteTestDatabase.cs` now uses `Pooling=False`, never `ClearAllPools()`).

> **Caveat the green count hides:** the UI-E2E tests populate screens by calling `LoadAsync`/`RefreshAsync` **themselves**;
> none asserts that *navigation* triggers a load. That is exactly why the P1 defect in §4 passes the suite. See §6 caveats.

## 3 · Real-data run (production composition, real drives)

An isolated harness composed the **production** host (`Bootstrap.BuildApp` → `ValidateOnBuild=true` + migrated SQLite),
registered both real repositories, drove the real SDK services, then drove the **production shell**. Verbatim (abridged):

```
══════ REGISTER — repositories ══════
  [OK]   registered D:\VarVault_test_repo  → tier T3, Hdd, online=True, cap=931.40 GB
  [OK]   registered E:\VarVault_test_repo_01  → tier T3, Hdd, online=True, cap=1863.00 GB

══════ INDEX — IndexOrchestrator.IndexAllAsync ══════
  …277 indexed, 0 skipped, 0 pruned, 1 corrupt, 2 unrecognized   (D:)
  …30 indexed, 0 skipped, 0 pruned, 0 corrupt, 0 unrecognized    (E:)
  repositories=2 indexed=307 resolved=259 missing=3574   elapsed 19.6s (16 vars/s)

══════ READ MODEL ══════  PackageListItems=274  VarFiles=307  HasMissingDeps=165
══════ DASHBOARD ══════  packages=274  size=25.89 GB  hot=0 warm=0 cold=274  missingDeps=165
══════ LIBRARY ══════  total=274; e.g. 爱发电shaob.柜姐.1  Scene 1231.6 MB  [Cl 35  Hr 14  Mo 7  As 3  Sc 1]; creators=108
══════ DEDUP ══════  exact within-identity groups=31 (e.g. ARCHER.JINGJUE2333.1 ×2 → 74.1 MB); near=0
══════ HEALTH ══════  encoding groups=1 (GBK×4); corrupt=1 (SolidVault.Lara_ROTR_SFM.1); missing-meta=2
══════ TIERING ══════  hot=0 warm=0 cold=274; policy Hot→T1 Warm→T2 Cold→T3; misplaced=0; stale=1; plan=0 moves

══════ GUI SHELL — navigation auto-load check (production AppHost.CreateShell) ══════
  screens exposing an explicit LoadAsync/LoadCommand: 9/13
  Dashboard after Navigate('dashboard'): HasSummary=False  (empty on arrival)
  Dashboard after explicit LoadAsync():   HasSummary=True  (wiring is real)
  [DEFECT] Navigate() does NOT auto-load screens — data appears only after an explicit LoadAsync.

══════ ACTIVATION — symlink materialization to VaM root (Developer Mode) ══════
  probe: F:\VaM_1.20.77.9\AddonPackages exists (symlink=True) — left UNTOUCHED (no profile switch).
  BuildProfileLinks: created=12 removed=0 missing=0 privilegeFailures=0
  on-disk under ___AddonPacksSwitch ___\VarVaultAuditRun\___VarsLink___ : 12 link files, 12 resolve to a real source var
    e.g. AKKEVE.hair_111.1.var  →  D:\VarVault_test_repo\___VarRedundant____removedFiles\AKKEVE.hair_111.1.var
  [OK]   materialized 12 REAL symlinks; rescued 12; removed app dir — VaM root left as found.

══════ RESULT ══════  PIPELINE: ALL STAGES PASSED ✅   GUI DEFECTS FOUND: 1 ⚠   (exit 0)
```

**What this proves on real data**
- **Engine works end-to-end.** Composition, indexing (307 vars / 2 drives / 1 corrupt / 2 unrecognized), dependency
  resolution (165 flagged), **31 exact content-signature duplicate groups**, real health detection (GBK / corrupt /
  missing-meta), real tiering + policy, per-content-type counts served from real data, and **12 real NTFS symlinks** in
  the real `F:\VaM` (each resolving to its source `.var`), created then rescued — the game's real `AddonPackages` symlink
  left untouched.
- **But the GUI shell does not auto-load** (next section).

## 4 · 🔴 P1 — Screens do not load their data on navigation

**Finding.** Driving the **production** shell (`AppHost.CreateShell`) over the real indexed catalog: navigating to a
screen swaps the view but **never loads its data**. Proven live — `HasSummary=False` immediately after
`Navigate('dashboard')`, `True` only after an explicit `LoadAsync()`. **9 of 13** screens expose a `LoadAsync`/`LoadCommand`
that nothing invokes on navigation.

**Root cause (code-verified).**
- `ShellViewModel.Navigate` ([ShellViewModel.cs:217-227](../../src/VarVault.App/ViewModels/ShellViewModel.cs#L217-L227))
  sets `ActiveScreen` + rail state only — no load call.
- `AppHost.CreateShell` never calls a screen's `LoadAsync`; startup enqueues a background `IndexAllAsync`
  ([AppHost.cs:174](../../src/VarVault.App/Composition/AppHost.cs#L174)) that fills the **DB**, but no screen is told to
  read it and **no screen subscribes to any event** (grep: zero `IEventBus`/`Subscribe` in the App).
- `MainWindow.OnLoaded` ([MainWindow.axaml.cs:19-29](../../src/VarVault.App/Views/MainWindow.axaml.cs#L19-L29)) starts a
  750 ms timer that refreshes **chrome only** (`RefreshLiveStateAsync` → jobs/badges/log-dock/pkg-count), not the screen.
- Screen views are bare `AvaloniaXamlLoader.Load(this)` with no `Loaded`/`DataContextChanged` load hook (only
  `SettingsView` wires a folder picker, and it does **not** load either).

**Per-screen impact.**

| Screen | In-screen way to populate it? | On fresh launch |
|---|---|---|
| **Library** | Yes — Refresh button, rail saved-views, any filter change | empty until first interaction — **recoverable** |
| **Trash** | Partial — "Back up now" loads as a side effect | empty until you click it |
| **Repositories** | No usable path (benchmark/set-tier need an existing card first) | **permanently empty** |
| Dashboard, Presets, Tiering, Dupes, Health, Missing, Proposals, Analytics, History, Settings | **none** | **empty / blank fields** |

**Secondary risk.** `SettingsViewModel.SaveAsync` writes `VamPath ?? ""`
([SettingsViewModel.cs:111](../../src/VarVault.App/ViewModels/SettingsViewModel.cs#L111)). Because Settings never
auto-loads, opening it (blank fields) and pressing **Save** overwrites the stored VaM path + policy with empty strings —
a latent config-wipe.

**Fix (small, systemic).** Invoke the active screen's `LoadAsync` on navigation (in `ShellViewModel.Navigate`) or on view
attach (`OnAttachedToVisualTree`/`DataContextChanged`), and subscribe screens to the index-complete event so they refresh
after the startup job. Add a UI-E2E test that asserts **navigation alone** populates a screen (today's tests call
`LoadAsync` directly, so they miss this).

## 5 · Design parity vs the draft HTML mockups

Both mockups read end-to-end; every region mapped to `src/VarVault.App` under **HR-G0**. Legend: **WIRED** (rendered +
real command/service) · **PARTIAL** (present, below-design) · **MISSING** · **DEAD** (rendered, no command) ·
**DEFERRED** (doc-24 gated). Everything on a non-Library screen is additionally subject to the §4 load defect.

**Screen-level:** all **13** mockup nav screens map to a **real** ViewModel in
[`AppHost.cs:29-50`](../../src/VarVault.App/Composition/AppHost.cs#L29-L50), each resolving a real SDK service. **13/13.**
**Dialogs:** all **10** modals have real dialog views + VMs, and (unlike screens) **do auto-load** via `DialogLauncher`.

### 5.1 Shell chrome — WIRED
Rail nav (13 screens, groups, active state, badges), global search + **Ctrl-K command palette**, searchable Creator combo
(108 creators live), facets, Gallery/Table toggle, theme, **Jobs tray** (progress + cancel + pause-all), **Rescue**
(exercised live), log dock, toast/undo, modal host. All bound to real commands/services.

### 5.2 Library screen (the mockup centerpiece)
| Element | Status | Evidence / note |
|---|---|---|
| Rail saved-views / tags | **WIRED** | `LibraryView.axaml:14-34` → real filter commands + `ITagService` |
| Rail maintenance / dependency-analysis links | **PARTIAL** | navigate-only (e.g. "Rebuild symlinks" just opens repos); 2 of 5 dep-scan variants |
| Creator combo · packageName · Installed · Reset · Sort · view toggle · search | **WIRED** | `LibraryViewModel` filters → `ILibraryQueryService` |
| Facet chips (types / tier / +Filter) | **PARTIAL** | only a removable creator chip; **no content-type or tier facet** on `LibraryQuery` |
| **Per-content-type columns Sc/Lk/Cl/Hr/Pl/As/Mo/Po/Sk** | **PARTIAL** | collapsed to ONE `ContentSummary` string ([LibraryView.axaml:154-156](../../src/VarVault.App/Views/LibraryView.axaml#L154)); data (`ContentCounts`) present + proven live, but not the 9-column grid |
| **Added / Used** date columns | **MISSING** | not in the row grid (`LastUsedAt` exists on the DTO) |
| **Installed** ● indicator | **MISSING** | no installed dot/column |
| Row select · Fix Var · Detail | **WIRED** | `ToggleSelection` / `FixRowCommand` / `OpenDetailCommand` |
| **Dep** column | **PARTIAL** | shown as a missing/ok **StatePill**, not a numeric column |
| Gallery cards | **PARTIAL** | thumbnail + type + name + creator only; **no tier/state/fav/installed pills, temp dot, or size** (all on the DTO) |
| Ops: **Install / Uninstall** (bulk) | **MISSING** | absent from the ops bar; activation is preset-only (doc-24 A12 gate) |
| Ops: Delete / Move / Add-to-preset / Fix / Export→txt / Install-from-txt | **WIRED** | real services (`EfLibraryActionService`), delete gated by the deletion predicate |
| Ops: "select all N matching" | **PARTIAL** | selects loaded rows only, not the whole match set |
| Detail: **★ Favorite** action | **DEAD** | [LibraryView.axaml:211](../../src/VarVault.App/Views/LibraryView.axaml#L211) — **no Command**; yet a Favorites view filters on the flag |
| Detail: **◎ Locate file** action | **DEAD** | [LibraryView.axaml:212](../../src/VarVault.App/Views/LibraryView.axaml#L212) — **no Command** |
| Detail: **content-previews strip** | **MISSING** | not in the Library detail (only a text list under the VarDetail dialog) |
| Detail: dependencies ok/sub/missing rows | **PARTIAL** | shows "depends on N / depended-on-by N", not per-dependency status rows |
| Detail: copies-across-tiers · resolve-via-alias · open-full-detail | **WIRED** | copies list + alias dialog + VarDetail dialog |

### 5.3 Other screens — all WIRED at the service level, all blocked by §4 (except Library/Trash)
Dashboard (reclaimable/classification/recent-activity/attention — but tier bars bind raw bytes into a 0..1 `MeterBar`,
so they read full; "wasting fast storage" on Analytics is always 0, `WastedOnFastBytes` never assigned), Tiering (4 tabs
+ policy + stale + plan), Dupes (Reclaim/Exact/Near/**Intake** tabs — A10), Health (Encoding/Integrity/Missing-meta),
Proposals (category filter), Missing deps (alias-column static "global"), Repositories (no read-speed/SMART fields on the
DTO), Presets (member list, activate/switch), Analytics (space-by-type; "usage over time" is a size sparkline not a
loads/day series), Trash, History, Settings (5 tabs, VaM-path validate + real picker). *(Both bind issues re-verified
first-hand: `WastedOnFastBytes` appears only in the XAML binding ([AnalyticsView.axaml:46](../../src/VarVault.App/Views/AnalyticsView.axaml#L46))
and is never assigned in any `.cs` → always "0 bytes"; the storage-by-tier `MeterBar` binds raw `UsedBytes`
([DashboardView.axaml:29](../../src/VarVault.App/Views/DashboardView.axaml#L29)) into a clamped 0..1 meter → reads full.
The classification bars correctly use `Hot/Warm/ColdFraction`.)*

### 5.4 `main-window.html`-specific surface not reproduced
- **AddonPackages profile selector** (rail dropdown + Add/Rename/Del) — **MISSING**; profile switching is only implicit via Presets "Activate & switch".
- **Activity-log dock** (colored [OK]/[INFO]/[WARN] live tail) — **MISSING**; only a 26 px chrome strip exists.
- Status-bar files-count / per-tier mini-bars — **PARTIAL**.

### 5.5 Parity summary
| Bucket | Approx. count of ~130 enumerated elements |
|---|---|
| **WIRED** | ~96 (~74%) |
| **PARTIAL** | ~19 (~15%) |
| **MISSING** | ~11 (~8%) — Install/Uninstall, content-previews strip, per-type columns, Added/Used, activity-log dock, AddonPackages selector, repo read-speed/SMART, extraction toggles, 5-way dep-scan |
| **DEAD** | 2 controls (Favorite, Locate) + orphan/legacy VMs |

**Structural parity ≈ 80%** (WIRED + half-credit PARTIAL). **Effective "works on first navigation" parity ≈ 55–60%**,
because §4 leaves ~9–11 of 13 screens empty until a load the UI never issues. The gap between those two numbers is almost
entirely the single auto-load defect. This is still a genuine lift over doc-23's 65–75% structural estimate — the dead
tab-sets, command palette, download-intake, Dashboard tiles, and per-content-type data all landed — but the effective
number is what a user sees.

**Implemented beyond the mockups:** command palette (Ctrl-K), startup-error window, real off-thread preview thumbnails,
move-to-subfolder + txt import/export, reverse-dependency delete warning, near-duplicate + download-intake classification,
real folder pickers, DI-scoped live feeds — plus engine depth the static mockups only hint at (durable multi-drive
migration, reference-counted symlink activation, CJK fold-key identity).

## 6 · Remediation verification — doc-23 findings (F-1…F-14) at HEAD

| Finding | Verdict | Evidence / proving test |
|---|---|---|
| **F-1** 4 decorative tabs now swap content | **CLOSED** | `Dupes/Health/Tiering/ProposalsViewModel` handlers + per-tab `IsVisible`; `GapATabWiringTests.*` |
| **F-2** DupeReview keep-selection | **CLOSED** | `DupeReviewViewModel` `SelectedCopy`→`KeepId`; `…user_can_choose_which_copy_survives` |
| **F-3** Library bulk Install/Uninstall | **DEFERRED / still MISSING in UI** | A12-DECIDE gate; a headline mockup action remains absent |
| **F-4** Per-scope DbContext for feeds | **CLOSED** | `ShellLiveFeeds` per-poll scope; `Concurrent_snapshots_never_share_a_dbcontext` |
| **F-5** Startup-error surface | **CLOSED** | `AppHost.TryCreateShellOrError` + `StartupErrorWindow`; `StartupErrorTests` |
| **F-6** Library counts + Dep + preview strip | **PARTIAL** | counts **data** done + proven live, but rendered as ONE column not 9; Dep = pill; preview strip **missing** |
| **F-7** Dashboard reclaimable/activity/classification | **WIRED but blocked by §4** | `DashboardViewModel` + view bound; renders empty on arrival (tier bar mis-normalized) |
| **F-8** Command palette / profile switcher | **CLOSED (palette)** / **DEFERRED (switcher)** | Ctrl-K + `CommandPaletteView`; `Command_palette_filters_by_query_and_invokes` |
| **F-9** smaller partials | **OPEN** (opportunistic) | Presets pills / Missing alias-col / Analytics series / Migrate table / Onboarding stepper still simplified |
| **F-10** hardcoded corpus paths → env vars | **CLOSED** | `TestCorpus.cs`; zero `VarVault_test_repo` literals in test logic |
| **F-11** flaky SQLite pool race | **CLOSED** | `Pooling=False`; **591/591 twice here** |
| **F-12** soft/tautological assertions | **CLOSED (corpus-gated)** | derive from real corpus counts; executed for real here |
| **F-13** echo-plumbing App tests | **mitigated** | real composed-host tests alongside (`GapBackendFlowTests`, `EfTieringServiceTests`) |
| **F-14** 7 orphan ViewModels | **DEFERRED** (6 remain; palette VM promoted) | tested dead code (E11) |
| **A10** Download-intake tab / `IIntakeService` | **CLOSED** | DI-registered; `DupesView` intake tab; `Intake_classifies_new_then_exact_duplicate` |

**12 closed · F-6 partial · F-7 wired-but-nav-blocked · 3 deferred-by-design (F-3, F-8-switcher, F-14) · F-9 open.** The
remediation was real. **New since doc 23:** the §4 auto-load defect (not previously recorded) and the confirmed **dead
Favorite/Locate** buttons.

### Caveats
- **Corpus-gated real-data tests** (F-10/F-12) only execute when `VARVAULT_TEST_CORPUS` is set (it was, here); otherwise
  they silently early-return (xUnit v2 has no `Assert.Skip`).
- **F-1 test altitude:** tab-swap tests assert VM booleans, not rendered `IsVisible` (bindings read-verified).
- **The suite never asserts navigation triggers a load** — the blind spot behind §4.

## 7 · Backend reality (do not re-litigate)

- **Zero** `NotImplementedException` / `NotSupportedException` / `TODO` / `FIXME` in `src/**/*.cs` (grep = 0).
- All six load-bearing algorithms present and exercised **on real data** in §3: ContentSignature dedup, missing-deps
  resolution, indexing orchestration, durable tiering/migration, deletion predicate, CJK fold-key.
- DI composes fully (`ValidateOnBuild=true` + `ValidateScopes=true`) — proven by the production `Bootstrap.BuildApp` run.

## 8 · Prioritized recommendations

**P1 — makes the app usable**
- **G-0 · Auto-load screens on navigation (§4).** Invoke `LoadAsync` in `ShellViewModel.Navigate` or on view-attach;
  subscribe screens to the index-complete event; add a UI-E2E test that navigation alone populates a screen. Also guard
  `SettingsViewModel.SaveAsync` from writing blanks before a load. **Highest impact, small fix.**

**P2 — visible parity gaps**
- **G-1 · Dead Favorite / Locate buttons** — bind commands (favoriting is otherwise impossible though a Favorites view exists).
- **G-2 · Library table: render the 9 per-content-type columns + Added/Used** (data already present as `ContentCounts`/`LastUsedAt`); a numeric **Dep** column; **content-preview strip** in the detail (gallery extraction already exists).
- **G-3 · Gallery cards: surface the tier/state/favorite/installed pills + size** (all on the DTO).
- **G-4 · Dashboard tier bars** normalize bytes to 0..1; **Analytics** assign `WastedOnFastBytes` / real loads-day series.

**P3 — deferred-by-design (decide, don't drift)**
- **G-5 · Library bulk Install/Uninstall (F-3/A12)** — route through an ephemeral preset + `BuildProfileLinksAsync` (the proven path).
- **G-6 · AddonPackages profile selector (E10)** and **G-7 · delete the 6 orphan ViewModels (E11)**.

**Hygiene**
- **G-8 · Bump the CVE-flagged transitive deps** (32 `NU1903`). **G-9 · xUnit v3** for real `Assert.Skip`.

## 9 · Bottom line

The **engine is done and honest** — proven on a real multi-drive corpus and a real VaM install, with the full suite green
twice and a clean `git` tree. The doc-23/24 GUI remediation **genuinely landed** (structural parity ~65–75% → ~80%). But
the app is **not yet usable as the design intends**, because a single systemic defect — **screens never load their data on
navigation** — leaves ~9–11 of 13 screens blank on arrival (effective parity ~55–60%). Fix that one wire (plus the two
dead detail buttons) and the GUI jumps to match the backend. Everything else is enumerated design polish or
deliberately-gated features; **nothing in the design is outright unbuildable, and nothing in the backend is fake.**
