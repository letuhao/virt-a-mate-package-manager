# 20 — Legacy Feature Gap Analysis (varManager → VarVault)

**Date:** 2026-07-20
**Old app:** `varManager-MMDLoader v1.0.1.0` (.NET Framework 4.8 WinForms, Access `.mdb`, single-process "God object" `Form1.cs`)
**New app:** `VarVault` (.NET 10 modular monolith, Avalonia, EF Core + SQLite, this repo)
**Method:** Source-verified inventory of both codebases (not checklists — those have been historically inaccurate per project notes), cross-referenced against `docs/varManager/` (legacy RE) and `docs/new-app/` (design + status). Every claim below traces to actual source.

---

## 0. How to read this

The comparison is split into four buckets so intent is never confused with omission:

1. **Deliberately out of scope for v1** (sealed in [CLAUDE.md](../../CLAUDE.md) + [10-Decisions-Log](10-Decisions-Log.md)) — *not* gaps. Listed so nobody re-flags them.
2. **Genuine gaps** — legacy capability the new app is supposed to have but doesn't, or has as dead/unwired code.
3. **Wrong / deviating implementations** — present but behaves differently from legacy in a way that matters.
4. **New app does it better** — where VarVault is a real upgrade (context for the effort).

**Bottom line up front:** VarVault's *backend engines are real and substantially better-engineered* than legacy — multi-drive tiered storage, content-hash dedup, durable migration, and a stronger CJK fixer are all genuine, wired, and tested. The gaps are concentrated in **(a) the runtime feed that makes tiering actually work, (b) per-var symlink installation to disk, (c) the CLI, and (d) several complete engines that exist but have no trigger/UI ("unwired").** None of the core new-value features are fake; several are inert because nothing calls them.

---

## 1. Deliberately out of scope for v1 — NOT gaps

Per CLAUDE.md ("Out of scope for v1 (sealed): Hub, load-into-VaM, MMD, packaging") these legacy areas are intentionally excluded. Flagging for completeness only:

| Legacy area | Legacy file(s) | Status in VarVault |
|---|---|---|
| **Hub integration** (browse/search hub.virtamate.com, ownership match, download-URL list generation, "scan hub for missing/updates") | `FormHub.cs`, `HubItem.cs` | Excluded by design |
| **MMD motion loading** (MikuMikuDance → VaM Timeline) | `MMDLoader/`, `LoadScene/` | Excluded by design |
| **Load-into-VaM / scene IPC** (`loadscene.json` wire contract, temp links, `feelfar` plugin hand-off, gender/person-order matching) | `Form1.LoadScene/GenLoadscenetxt`, `InstallTemp`, `DeleteTempThread` | Excluded by design |
| **Scene analysis + preset extraction** (`FormAnalysis` tri-state atom tree, slice a Person atom into `.vap`/`.bin` presets) | `FormAnalysis.cs`, `Form1.AnalysisAtoms/ReadSaveName` | Excluded (tied to load-into-VaM) |
| **Packaging / PrepareSaves** (crawl a save's dep closure, produce a copy-list for packaging) | `PrepareSaves.cs` | Excluded by design (was itself a stub in legacy) |

> ⚠️ If any of these are later pulled back into scope, they are **large** — the load/analysis/MMD path is ~40% of the legacy codebase by line count and depends on a VaM in-game plugin.

---

## 2. Genuine gaps (should have, but don't)

Ordered by impact. **G1–G3 are the ones that actually degrade the product today.**

### G1 — Usage feed is missing → tiering is structurally inert ⚠️ **highest impact**
The headline new-value feature is *usage-driven hot/warm/cold placement across drives*. The scoring engine is real and good (`Domain/Analyzer/UsageScoring.cs` — recency+frequency+centrality, hysteresis, cooldown) and the recompute pass runs (`Infrastructure/Indexing/EfUsageAnalyzer.cs`, called by the orchestrator).

**But nothing ever records a usage event.** `IUsageAnalyzer.RecordAsync` has an interface, an impl, and tests — **no production caller** (verified: grep shows only the definition sites). The `UsageSource.VamLogImport` enum value (`Domain/Entities/Enums.cs:73`) is defined but **nothing implements a VaM `output_log.txt` parser**, and the Library rail's "Analyze VaM log" action is not wired.

**Consequence:** `UsageEvents` stays empty → every package scores 0 → everything settles to `ContentClass.Cold` → "misplaced / hot on cold drive / cold on SSD" detection has no signal to work with. The tiering UI renders, but the placement recommendations are effectively meaningless until a feed exists.

Legacy had no automated usage feed either (it used manual hide/fav sidecars + `LogAnalysis` of `output_log.txt` for *missing-dep* auto-install only), so this is not a *regression* — but it is the gap between "tiering works" and "tiering is a demo." **This should be the #1 build item.** A VaM-log importer is the natural producer and legacy already proves the log format is parseable (`Form1.LogAnalysis`).

### G2 — Preset activation never materializes per-var symlinks on disk ⚠️ ~~**the install feature is non-functional**~~ → **FIXED 2026-07-20**
*(Re-verified 2026-07-20 against the live install `F:\VaM_1.22.0.3` + full source trace of the activation path.)*

> **✅ RESOLVED** — `EfActivationService` now materializes real per-var symlinks on disk (install links in `___VarsLink___`, alias links in `___MissingVarLink___`) named by the verbatim `Creator.Package.Version.var` identity, with `vamRoot` resolved from `SettingKeys.VamPath`, an idempotent create/diff/orphan-sweep, atomic privilege abort, and disk-level deactivate/rescue/temp. GUI: Presets **Activate**/**Activate & switch** + VaM-path guard; Settings **Browse** + install validation; the dead symlink-strategy dropdown removed; reconcile-on-startup. Proven by a real-data E2E on the 277-var corpus (5 members → 8 links via closure, all identity-named, all targeting real files, idempotent) plus xUnit coverage. See **[22-Activation-Symlink-Install-Implementation-Checklist](22-Activation-Symlink-Install-Implementation-Checklist.md)**. One operational note: symlink creation needs Windows **Developer Mode / elevation** (same as legacy) — handled with a clear runtime hint. The description below is retained as the historical problem statement.

**Legacy install is two distinct on-disk symlink mechanisms** (both confirmed on the live install):
1. **Profile switch** — `AddonPackages` is a *directory* symlink → `___addonpacksswitch ___\{profile}` (live: 13 profiles, active = `ng9`). Switching = repoint that one link.
2. **Per-var install** — inside the active profile, `___VarsLink___\` holds one *file* symlink per installed var, named `Creator.Package.Version.var` → the real repo file (live: **3,302 links** → `F:\AddonPackages\___VarTidied___\...`). Plus `___MissingVarLink___\` alias links for missing deps. This is *the* core action — it's how vars become loadable in VaM without copying (`Form1.VarInstall`, closure-expanded, cap 500, `.disabled` markers).

**What VarVault actually does:**

| Legacy mechanism | VarVault | Verdict |
|---|---|---|
| VaM-folder setting | `SettingKeys.VamPath = "vam.path"`, editable in Settings | ✅ present |
| Profile switch (repoint `AddonPackages` dir symlink) | `PresetsViewModel` → `EfProfileService.SwitchToAsync` → `VamProfileService.SwitchTo` → `SymlinkService.RepointDirectory` | ✅ **real, on disk, wired** — matches legacy exactly |
| Per-var install symlinks in `___VarsLink___` | `EfActivationService.RecomputeAsync` writes `ActivationLink` **DB rows** + `SaveChangesAsync`; **never calls `SymlinkService.CreateFile`** (grep: **zero callers** of `CreateFile` anywhere) | ❌ **not materialized** |
| `___MissingVarLink___` alias links | aliases live in `VarAliases`, folded into the closure, never written to disk | ❌ DB-only |

**Consequence:** point VarVault at the VaM folder and profile-switching works, but **"activating" a loading preset produces no on-disk var links** — VaM opens the profile and sees an essentially empty `AddonPackages`. The member vars exist only as database rows asserting "a symlink should be here." **The install feature does not work.** This is more than the sealed **BE-G1** "no per-var buttons" deviation — the activation builder itself is a no-op on the filesystem.

**Three concrete defects in `EfActivationService` ([EfActivationService.cs:111-120](../../src/VarVault.Infrastructure/Indexing/EfActivationService.cs#L111-L120)), beyond "unwired":**
1. **No filesystem call** — `RecomputeAsync` only `db.ActivationLinks.Add(...)`; nothing ever calls `SymlinkService.CreateFile`/`CreateDirectory` to place a `.var` link.
2. **Wrong link filename** — `LinkPath = "{profile.DirPath}/___VarsLink___/{packageId}.var"` uses the **numeric DB id** (e.g. `4213.var`), not the required `Creator.Package.Version.var`. VaM identifies packages *by that filename* — a numeric name would break VaM's own dependency resolution even if the link existed.
3. **No VaM root** — `profile.DirPath` is a **relative** string `"___AddonPacksSwitch ___/{name}"`; `EfActivationService` never reads `SettingKeys.VamPath` (unlike `EfProfileService`), so it has no idea where the real VaM folder is.

The primitives are sound (`SymlinkService.CreateFile`/`RepointDirectory` are Developer-Mode-aware and reparse-safe). The activation builder needs real work — materialize links via `CreateFile`, use the identity filename, resolve `vamRoot` from settings, materialize alias links, and support deactivate/rescue against the real filesystem — spec'd in **[21-Activation-Symlink-Install-Spec](21-Activation-Symlink-Install-Spec.md)**.

### G3 — CLI is a stub
`VarVault.Cli/Program.cs` is 8 lines — it prints loaded module names and exits. CLAUDE.md advertises `dotnet run --project src/VarVault.Cli` as a headless entry / smoke path. There is no `index`, `migrate`, `repo add`, or any command. Legacy had no CLI, so not a regression, but it's advertised and empty. Low effort, useful for scripted re-index / CI smoke.

### G4 — Complete engines that exist but are never called ("unwired")
These are *implemented* (real logic, DI-registered in most cases) but have **no production trigger** — so their capability is unreachable in normal use. Each is a small wiring job, not a build-from-scratch:

| Engine | File | Legacy equivalent it replaces | Why it matters |
|---|---|---|---|
| **UserSaveScanner** | `Infrastructure/Indexing/UserSaveScanner.cs` | `Form1.FixSavseDependencies` / `savedepens` table; `PrepareSaves` crawl | Without it, a var needed only by the user's own loose saves looks orphaned. Legacy explicitly protected these (`DependentSaved`, `GetDependents`). **Orphan/missing accuracy is wrong until this runs.** |
| **CatalogReconciler** | `Infrastructure/Indexing/CatalogReconciler.cs` | `UpdDB`'s prune-vanished-rows self-heal | DB can drift from disk with no reconciliation trigger. |
| **RepositoryWatcher** (FS change watch) | `Infrastructure/Indexing/RepositoryWatcher.cs` | (none — legacy was manual `UpdDB`) | Not even DI-registered. Would enable incremental auto-index. |
| **VarManagerImport** (legacy `.fav`/`.hide`/quarantine import) | `Domain/Import/VarManagerImport.cs` | reads legacy sidecar state | **Migration path for existing legacy users is dead** — no caller, no GUI trigger. Anyone moving from varManager loses their hide/fav curation. |

### G5 — Hide / Favorite curation not surfaced
Legacy's only "favorites" mechanism is the three-state **hide / normal / fav** per scene, stored as sidecar files (`{scene}.hide` / `.fav`) under `AddonPackagesFilePrefs`, with drag-drop between three lists (`FormScenes`). VarVault has `VarManagerImport` to *read* legacy fav/hide state (G4) but **no equivalent curation feature** of its own and no UI to set/browse hide/fav. If curated browsing matters to users, this is a missing capability (legacy users will notice its absence immediately). Note: legacy also has **no ratings and no tags** — so those are not gaps, just never-existed.

### G6 — Install-from-txt exists; export-installed-list not confirmed wired
Legacy had a symmetric pair: **Export installed list** → text file (`buttonExpInsted_Click`) and **Install from txt** → install each line verbatim (`buttonInstFormTxt_Click`). VarVault's Library ops-bar has **install-from-txt** (`LibraryViewModel`), and preset txt import/export exists (`PresetImporter`/`PresetTextFormat`), but a plain "export currently-installed/active list to txt" round-trip wasn't confirmed. Minor, but the pair is a known legacy workflow. Verify and add the export half if absent.

### G7 — Stale / old-version cleanup: policy-only, no measured input
Legacy `StaleVars` / `OldVersionVars` (`FormStaleVars`) physically moved superseded versions to `___StaleVars___` / `___OldVersionVars___`, with a "not depended upon" guard and a content-vs-plugin heuristic. VarVault has the `ContentClass`/placement machinery and a Trash/reclaim path, but "stale/cold" is currently a *default* (see G1 — no usage signal) rather than a *measured* state. Superseded-version cleanup as a distinct, guarded action wasn't confirmed as a first-class feature. Confirm coverage or add a version-supersession sweep.

---

## 3. Wrong / deviating implementations (present, behaves differently)

### D1 — Catalog writes bypass the single-writer queue (violates own convention)
CLAUDE.md is emphatic: **"All catalog writes → `IWriteQueue`** (single-writer; SQLite is single-writer)." In practice, most `Ef*ActionService` classes call `DbContext.SaveChangesAsync` **directly**; only `EfLibraryActionService.MoveToSubfolderAsync` routes through `IWriteQueue` (confirmed via doc 19 AC-31 + source). WAL + a separately-scoped indexer keep this from exploding, but the single-writer discipline the architecture is built around is **not uniformly enforced**. This is a correctness/consistency risk under concurrency and a direct deviation from a stated load-bearing rule. Either route all action writes through the queue or amend the rule.

### D2 — "Install/Uninstall" semantics changed (see G2)
This is both a gap (no disk symlink) and a deviation: legacy install semantics (per-var symlink + closure + disabled markers + 500-cap confirm) were replaced by DB-row "preset activation." Flagged under G2; noting here so the *behavioral* divergence isn't missed by someone expecting legacy semantics.

### D3 — Dedup semantics are *stronger* but not equivalent — verify redundant-folder parity
Legacy dedup is **filename-only** (`___VarRedundant___` on name collision during Tidy) plus name-idempotent indexing. VarVault does real **content-hash dedup** (`ContentSignatureEngine`, three fingerprints, order/compression-invariant) with a safety predicate. This is strictly better (see §4), **but** the legacy behavior of "same filename ⇒ quarantine to redundant folder during organize" is a different axis. Confirm VarVault's organize/import path still handles pure filename collisions (two different-content vars with the same name) sanely, since content-hash dedup won't merge those.

### D4 — Indexing freshness model differs (this one is an improvement, noted for awareness)
Legacy `UpdDB` is **idempotent by name** — once a var row exists, editing the var's *contents* is never re-indexed (a real legacy bug). VarVault skips-unchanged by **(size, mtime)** and re-inspects on change (`IndexingService`). Better — but means behavior differs if anything relied on legacy's name-only staleness. No action needed; documented so it's not mistaken for a regression.

---

## 4. Where VarVault is genuinely better (context)

These are real, wired, and (per doc 19) tested against the 277-var corpus — worth stating so the gaps above are weighed against substantial wins:

- **Multi-repo across multiple physical drives with tiered placement** — the entire reason for the rewrite. Real drive profiling via kernel32 IOCTL (media type, capacity, volume serial), tier policy, re-point with **volume-serial guard** (`RepositoryService`, `DriveProfiler`, `TierPolicy`). Legacy was **single repo path, single drive**, `File.Move` (fails across volumes).
- **Content-hash dedup** (`ContentSignatureEngine`) vs legacy filename-only.
- **Durable cross-drive migration** — copy → WriteThrough flush → re-read-and-hash verify → atomic rename → re-point refs → trash source, idempotent resume, `synchronous=FULL` around the destructive step (`DurableFileMover`, `MigrationRunner`). Legacy had none of this.
- **Stronger CJK fixer** — round-trip vote across GBK/Shift-JIS/Big5/EUC-KR/**GB18030**, re-encode to a validated UTF-8 zip, atomic rename, original untouched (`EncodingHealthEngine`, `EncodingFixer`). Legacy shelled out to a hardcoded `C:\Program Files\7-Zip\7z.exe` with size-only verification.
- **Fold-key identity** (NFC + case-fold, `IdentityFold`) applied pervasively — legacy matched raw strings in places.
- **Modular architecture + real persistence** (EF migrations, WAL, health checks, telemetry, single-writer queue *design*) vs one 3,743-line `Form1.cs` over an in-memory Access DataSet.
- **Real dependency resolver** with materialized `HasMissingDeps`, reverse-dep counts, `IsFoundational` — vs legacy's regex-over-raw-text extraction (which VarVault also does, but backs with a proper catalog).

---

## 5. Prioritized recommendation

| Pri | Item | Section | Effort | Why |
|---|---|---|---|---|
| **P0** | VaM-log usage importer → feed `IUsageAnalyzer.RecordAsync` | G1 | M | Without it the flagship tiering feature is inert. Legacy proves the log is parseable. |
| ~~**P0**~~ ✅ | ~~Make preset activation materialize per-var symlinks on disk~~ **DONE** — see [22](22-Activation-Symlink-Install-Implementation-Checklist.md); 576 tests green + real-corpus E2E | G2/D2 | M | Install now materializes VaM-correct symlinks; also routes activation writes through `IWriteQueue` (partial **D1**). |
| **P1** | Wire `UserSaveScanner` into the index/resolve pipeline | G4 | S | Orphan/missing accuracy is wrong without it. |
| **P1** | Enforce `IWriteQueue` for all action writes (or amend the rule) | D1 | S–M | Direct violation of a load-bearing convention. |
| **P1** | Wire `VarManagerImport` + a "migrate from varManager" trigger | G4/G5 | S | Legacy users currently lose all hide/fav curation on migration. |
| **P2** | Wire `CatalogReconciler`; register + wire `RepositoryWatcher` | G4 | S | Self-heal + incremental auto-index. |
| **P2** | Flesh out CLI (`index`, `migrate`, `repo add`) | G3 | S | Advertised, empty; enables CI smoke. |
| **P2** | Hide/fav curation UI; export-installed-list; version-supersession sweep | G5/G6/G7 | S–M | Legacy-parity conveniences. |
| **P3** | Confirm filename-collision handling in organize/import (dedup parity) | D3 | S | Edge case content-hash dedup doesn't cover. |

**Legend:** S ≈ hours–1 day, M ≈ few days.

---

*Generated by source-diffing `F:/varManager-MMDLoader_v1.0.1.0` against this repo's `src/`. Load-bearing legacy behaviors preserved/verified against `docs/varManager/`. Intentional v1 exclusions per CLAUDE.md + `10-Decisions-Log.md`.*
