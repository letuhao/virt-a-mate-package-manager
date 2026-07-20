# 23 — Full-Stack Completeness Audit (ground-truth, doc-independent)

**Date:** 2026-07-20 · **Scope:** `src/**` + `tests/**` + `mockups/*.html` vs. the completion claims in docs 07/09/16/18/19.
**Method:** doc-independent. Built the solution, ran the full test suite **twice**, traced the production
composition path (`App.axaml.cs` → `AppHost` → `Bootstrap.BuildApp` → SDK → SQLite), and ran four parallel
adversarial deep-dives (backend reality · GUI runtime wiring · HTML-mockup-vs-app parity · test authenticity).
Every sharp claim below was re-verified by hand with `file:line` evidence. **The checklist docs were treated as
suspect, not as truth.**

> **Why this doc exists.** Docs 09/16/18/19 claim **100% complete** (208/208 · 63/63 · 45/45 · 34/34). That is
> **true for the backend and false for the GUI-vs-design layer.** The old project-memory alarm ("static shell,
> indexing dead, `HasMissingDeps=false`") is **stale** — it predates the `996a81b…cd5eb01` fix commits, which were
> genuine build-out. This doc records what is *actually* real, and the specific, enumerable gaps that remain.

> **Remediation status (2026-07-20).** Tracked in
> [24-Gap-Remediation-Implementation-Checklist.md](./24-Gap-Remediation-Implementation-Checklist.md). **Landed with tests:**
> F-1 (4 decorative tabs live), F-2 (DupeReview keep-selection), F-4 (per-scope live-feed DbContext), F-5 (startup-error
> window), F-6 (Library content-type counts — no migration needed, `PackageContentCount` already existed), F-7 (Dashboard
> reclaimable/activity/classification bar), F-8 (command palette + download-intake tab), F-10/F-11/F-12 (corpus paths →
> env vars, flaky SQLite race fixed, soft assertions tightened), plus new backend for near-dup / missing-meta / tiering
> policy+stale. **Open by design:** F-3 **bulk install** (A12-DECIDE — re-architects the committed activation subsystem's
> symlink path) and a standalone profile-switcher (E10); both live in the activation session's just-committed code —
> coordinate, don't clobber. Minor: detail preview strip (E5), orphan-VM deletion (E11, left as tested dead code).

## Verdict summary

| Layer | Reality | Basis |
|---|---|---|
| **Backend / domain logic** | **Real & complete.** All 6 load-bearing algorithms implemented with real edge-case handling; every SDK interface → a registered concrete impl; **zero** `NotImplementedException`/stub/`TODO` in `src`. | Read + test-verified |
| **GUI runtime wiring** | **Functionally wired.** Composes (`ValidateOnBuild=true`), 13/13 real screens (0 runtime placeholders), commands hit real services, dialogs open from real buttons, live feeds poll on a 750 ms timer. | Ran the UI-E2E |
| **GUI parity vs mockups** | **≈ 65–75%.** Whole sub-features are UI-unreachable behind dead tabs; several screens partial; a few designed features have no UI at all. | XAML-verified |
| **Test suite** | **Real core, inflated green count.** 563 real methods; all 6 algorithms truly covered — but ~14 silently no-op, some assertions are soft/tautological, one is flaky. | Re-ran + re-read |

**Plain answer to "is it done?"** The engine is done and honest. The app *runs and is wired*. What is **not**
done is the GUI faithfully surfacing the design, and the test suite honestly reporting its own coverage.

---

## What is genuinely real — do NOT redo

Verified first-hand, so future audits don't re-litigate:

- **Build:** `dotnet build VarVault.slnx` → **0 errors** (~150–170 style-only warnings).
- **Backend is not hollow.** All six load-bearing algorithms are real:
  - ContentSignature dedup — `Domain/Fingerprinting/ContentSignatureEngine.cs:26` (sorted multiset of length-prefixed `(rawNameBytes, size, CRC-32)`; Zip64-aware raw-bytes reader in `Infrastructure/Indexing/ZipCentralDirectoryReader.cs`), consumed by `Domain/Dedup/DedupGrouping.cs` + `Infrastructure/Library/EfReclaimService.cs`.
  - Missing-deps — **the memory's "hardcoded false" is GONE.** Computed in `Infrastructure/Indexing/EfDependencyResolver.cs` (`UpdateHasMissingDepsAsync`) + `EfCatalogStore.cs:270-271` from real resolved rows.
  - Indexing orchestration — `Modules.Indexing/IndexOrchestrator.cs:17` (index online+enabled repos → resolve deps → recompute usage), triggered in prod by `AppHost.EnqueueIndexAll` at startup + after add-repo.
  - Tiering/migration — `Infrastructure/Indexing/DurableFileMover.cs:17` (copy `.partial` → WriteThrough+flush → **re-read & full-SHA-256 verify** → atomic move) + `MigrationRunner.cs` durable state machine (re-points canonical/activation before trashing source; never hard-deletes).
  - Deletion predicate — `Domain/Dedup/DeletionPredicate.cs:42` (blocks unless own hash verified **and** another same-identity online copy with equal full hash exists; cross-identity never authorizes). Gates every delete path.
  - CJK fold-key — `Domain/Identity/IdentityFold.cs:29` (NFC → ToUpperInvariant → NFC), used as the identity key everywhere.
- **DI graph resolves.** All ~19 shell-required services are registered in `Infrastructure/Persistence/PersistenceRegistration.cs:49-72`; `VarVaultHost.Build` uses `ValidateOnBuild=true` + `ValidateScopes=true`, so composition resolves fully or throws at build.
- **Shell is not a static shell.** `ShellViewModel.AllScreens` = 13; `AppHost.CreateShell` gives all 13 a **real** ViewModel (the `TryAdd` placeholder loop adds nothing). `ShellWiringE2ETests` drives the **production** `AppHost.CreateShell` over a real seeded DB and asserts real behavior (selection-count tracking, top-bar search filtering to 1 item).
- **Architecture rules are enforced** by `tests/VarVault.Architecture.Tests` (NetArchTest, real assembly-dependency assertions), 4/4 green.
- **Corpus is real.** E2E indexes `D:\VarVault_test_repo` = **277 vars**.

---

## Evidence base (test runs I actually executed)

| Assembly | Run 1 | Run 2 (clean) |
|---|---|---|
| Architecture | — | 4 / 4 |
| Common | — | 184 / 184 |
| Host | — | 8 / 8 |
| Infrastructure | **107 / 108 (1 FAIL)** | 108 / 108 |
| E2E | 68 / 68 | 68 / 68 |
| App | 191 / 191 | 191 / 191 |
| **Total** | **flaky** | **563 / 563 green** |

Same build, two outcomes → **the suite is not reliably green** (see F-11).

---

## Findings — severity-ordered, evidence-gated

Legend: **P0** functional bug (broken/dead) · **P1** feature unreachable · **P2** latent bug · **P3** robustness ·
**P4** below-design · **P5** test integrity · **P6** dead code.

### P1 — Features that render but are unreachable

- **F-1 · Decorative tabs on 4 screens.** `DupesViewModel:15`, `HealthViewModel:15`, `ProposalsViewModel:42`,
  `TieringViewModel:15` each declare `_selectedTabIndex` with **no `OnSelectedTabIndexChanged` handler and no
  `IsXxxTab` computed properties** (contrast the *working* `SettingsViewModel`/`TrashViewModel`/`VarDetailViewModel`).
  The tab strips switch but nothing reacts → **Lifecycle rules, Placement policy, Stale versions, Near-duplicates,
  Download-intake, Integrity/corrupt, Missing-meta, and proposal category filtering are all UI-unreachable** while
  looking implemented. *Highest-impact, lowest-effort gap.*
- **F-2 · DupeReview cannot choose which copy to keep.** `DupeReviewViewModel.KeepId` defaults to the first copy
  (`DupeReviewViewModel.cs:21`) and `Views/DupeReviewDialog.axaml` binds **no** control to `KeepId` → the user always
  auto-keeps copy #1. (Not data-loss — `DeletionPredicate` still guards safety — but the decision surface is missing.)
- **F-3 · No bulk Install / Uninstall in the Library ops bar.** `LibraryViewModel` has only `InstallFromTxtAsync`
  (a different "install from txt list" feature). The symlink activate/deactivate backend (`EfActivationService`)
  exists but is never surfaced as a library action — the two primary actions in the mockup.

### P2 — Latent runtime bug (won't show in tests)

- **F-4 · One `DbContext` shared for the whole app, hit concurrently by the poll timer.** `AppHost.TryCreateShell`
  backs all screen read-services with a single never-disposed scope (`AppHost.cs:136`); the 750 ms live-feed
  `DispatcherTimer` calls 4 EF query services on that same scoped `VarVaultDbContext` concurrently with
  user-triggered screen loads. `DbContext` is **not thread-safe** → possible intermittent *"a second operation
  was started on this context"*. Tests miss it because they use fresh per-operation scopes. Fix: give the feeds
  (and ideally each screen load) its own scope, or serialize snapshot reads.

### P3 — Robustness

- **F-5 · Silent blank window on startup failure.** `AppHost.TryCreateShell` wraps composition in
  `catch (Exception) { return null; }` (`AppHost.cs:141`); `App.axaml.cs` has no fallback UI, so a real composition
  error renders chrome bound to a **null DataContext with no error message**. Surface the exception (error view /
  log / dialog) instead of swallowing it.

### P4 — Screens present but below the design

- **F-6 · Library** table dropped the per-content-type count columns (Sc/Lk/Cl/Hr/Pl/Mo) **and** the **Dep** column —
  the defining visual of both mockups; the detail panel has **no hero image / content-preview strip**.
- **F-7 · Dashboard** lacks the Reclaimable headline stat + breakdown, the live Recent-activity list, and the
  stacked classification bar (renders a button / generic bars instead).
- **F-8 · Absent features with no UI at all.** **Command palette (Ctrl-K)** — `CommandPaletteViewModel` exists but
  there is **no `CommandPaletteView`** and it is not in `MainWindow.axaml` → dead. **AddonPackages profile switcher**
  — `IProfileService` is injected into `PresetsViewModel` but **no view surfaces it**. **Migrate dialog** shows a
  move *count* but no moves table; **Onboarding** has no visual stepper / benchmark-table columns.
- **F-9 · Smaller partials:** Presets member list has no Resolution/State pills; Missing screen has no "alias-to-owned"
  column (Scope hardcoded "global"); Analytics "usage over time" is a static type-size spark-bar, not a loads/day
  series; Activity action is plain text (no colored tag).

### P5 — Test-suite integrity

- **F-10 · 14 silent no-op "skips", zero `Assert.Skip`.** Corpus/drive/privilege tests use
  `if (!Directory.Exists(...)) return;` → they report **green while testing nothing** when the path is absent.
  On this machine the `E:\…repo_01`, `F:\…repo_02`, `G:\…repo_03` dirs **exist but are empty (0 vars)**, so the
  `.Any()`-guarded cross-drive tests (`IndexOrchestratorFlowTests.cs:73`, `OnboardingServiceFlowTests.cs:48`,
  etc.) **no-op'd even inside the "68/68 green" E2E run.** Convert every guard to `Assert.Skip(...)` so masked
  environments are visible.
- **F-11 · Flaky test.** `tests/VarVault.TestKit/SqliteTestDatabase.cs:32` calls the **process-global**
  `SqliteConnection.ClearAllPools()` on per-instance `Dispose()` while xUnit runs other classes in parallel →
  intermittent `ObjectDisposedException` on `SQLitePCL.sqlite3` (`EfSettingsServiceTests` failed run-1, passed
  run-2, identical build). Fix: don't clear global pools per-instance (use a pooled connection owned by the test DB,
  or disable pooling for the test connection string).
- **F-12 · Soft / tautological assertions under-verify.** The flagship 277-var indexing test asserts only
  `result.Indexed > 50` (`IndexingFlowTests.cs:149`) — a regression dropping 220 vars would still pass; and
  `IndexingFlowTests.cs:159-160` is `Assert.Equal(total − nullCount, nonNullCount)`, **true by definition**.
  `RealRepoDedupTests.cs:53` hides its real dedup assertion behind `if (count > 0)`. The checklist's "277 vars"
  wording overstates what is checked.
- **F-13 · ~24% of App.Tests (~45/191) are echo-plumbing** — a stub returns canned data and the test asserts a VM
  property/TextBlock echoes it. **Mitigated:** the same services also have real composed-host tests
  (`GapBackendFlowTests`, `EfTieringServiceTests`), so it's stub *alongside* real, not *instead of*. Low priority.

### P6 — Dead code

- **F-14 · 7 orphan ViewModels** never instantiated and with no view: `ConfirmViewModel`, `DedupReviewViewModel`,
  `HealthReportViewModel`, `JobsViewModel`, `ReclaimViewModel`, `UndoToastViewModel`, `CommandPaletteViewModel`
  (the last is the dead half of F-8). Superseded by shipped equivalents — vestigial, safe to delete once F-8's
  palette decision is made.

---

## Where the prior docs overstate (doc-lie ledger)

- **`07-UI-Coverage-Map.md:54-55`** marks *"Content-type counts → Library table count columns ✅"* and
  *"Preview images → detail preview strip ✅"* — **both absent in code** (F-6). Direct false ✅.
- **Docs 16 / 18 / 19** report **63/63 · 45/45 · 34/34** done, but items counted complete are partial (decorative
  tabs F-1) or served by different code than described. Doc 19's own preamble concedes doc 18 was "overstated" — the
  same is now true of the parts of 16/18/19 covering F-1/F-2/F-3/F-6/F-7.
- **`09-Implementation-Checklist.md` (208/208) is the honest one** — it matches the backend code.
- **General rule going forward:** honor **HR-G0 (doc 19): "Wired, not drawn."** A tab/control counts as done only
  when switching it changes state *and* a test proves the wire executes.

---

## Remediation checklist (evidence-gated; check off only with a test that proves the wire)

**Priority 0 — unreachable features (do first):**
- [ ] **R-1** Wire the 4 decorative tab sets (F-1): add `OnSelectedTabIndexChanged` + `IsXxxTab` props to
  `Dupes/Health/Proposals/TieringViewModel`, bind each tab's panel, implement the previously-hidden sub-views.
- [ ] **R-2** DupeReview keep-selection (F-2): bind a radio/selected-row control in `DupeReviewDialog.axaml` to `KeepId`.
- [ ] **R-3** Library bulk Install/Uninstall (F-3): add commands on `LibraryViewModel` → `IActivationService`, bind ops-bar buttons.

**Priority 1 — correctness / robustness:**
- [ ] **R-4** Per-scope DbContext for feeds/screen loads (F-4), or serialize `ShellLiveFeeds.SnapshotAsync`.
- [ ] **R-5** Replace the silent `catch → null` with a visible error surface (F-5).

**Priority 2 — test integrity:**
- [ ] **R-6** Convert all 14 `if(!Exists) return;` guards to `Assert.Skip(...)` (F-10).
- [ ] **R-7** Fix `SqliteTestDatabase` global-pool disposal race (F-11).
- [ ] **R-8** Tighten `IndexingFlowTests.cs:149/159-160` + `RealRepoDedupTests.cs:53` to real bounds (F-12).

**Priority 3 — design parity + cleanup:**
- [ ] **R-9** Library count columns + Dep column + detail preview strip (F-6); Dashboard stats/activity/classification (F-7).
- [ ] **R-10** Decide Command palette + Profile-switcher: build the view or delete the VM (F-8); then remove the 7 orphan VMs (F-14).
- [ ] **R-11** Smaller partials (F-9) as capacity allows.
- [ ] **R-12** Correct the false ✅s in doc 07 and reconcile 16/18/19 wording (doc-lie ledger).

---

## Reproduce
```
dotnet build VarVault.slnx                 # 0 errors
dotnet test  VarVault.slnx                 # run twice — F-11 is intermittent
```
Grep proofs: decorative tabs → `SelectedTab` in `src/VarVault.App/ViewModels` (4 have no handler);
silent skips → `Directory\.Exists.*return` in `tests` (14 hits, 0 `Assert.Skip`);
doc-lie → `07-UI-Coverage-Map.md:54-55`.
