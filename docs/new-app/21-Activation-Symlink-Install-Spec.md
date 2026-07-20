# 21 — Activation & Symlink-Install Spec (fix G2)

**Status:** Draft for build
**Date:** 2026-07-20
**Fixes:** [20-Legacy-Feature-Gap-Analysis](20-Legacy-Feature-Gap-Analysis.md) **G2** (and the D2 deviation)
**Touches:** `Domain/Activation`, `Infrastructure/Activation`, `Infrastructure/Indexing/EfActivationService`, `Sdk/Activation`, `VarVault.App` (Presets/Library), `Settings`
**Reference behavior:** `Form1.VarInstall` / `UnintallVars` / `FixRebuildLink` / `Createlink` (FormMissingVars) in `F:/varManager-MMDLoader_v1.0.1.0`; live install `F:\VaM_1.22.0.3`.

---

## 1. Problem (what's broken today)

Preset "activation" is a **filesystem no-op**. `EfActivationService.RecomputeAsync` resolves the member+dependency closure, picks the hottest online copy of each package, and inserts `ActivationLink` **database rows** — but nothing ever creates the actual `.var` symlinks in the profile's `___VarsLink___\` folder that VaM reads. Verified defects:

1. **No filesystem call.** `SymlinkService.CreateFile` has **zero callers** in the codebase.
2. **Wrong link filename.** `LinkPath = "{DirPath}/___VarsLink___/{packageId}.var"` uses the numeric DB id, not the VaM identity `Creator.Package.Version.var`.
3. **No VaM root.** `EfActivationService` never reads `SettingKeys.VamPath`; `Profile.DirPath` is a relative string, so links can't be placed under the real VaM folder.
4. **No GUI trigger.** `BuildProfileLinksAsync`/`DeactivateAsync` have no production caller (only `RescueAsync` and profile-`Switch` are wired). There is no "Activate" action.
5. **Alias links are DB-only.** `VarAliases` fold into the closure but `___MissingVarLink___\` links are never written.
6. **Two disjoint notions of "profile".** `VamProfileService` creates/lists/switches real directories under `___AddonPacksSwitch ___` (keyed by name); `EfActivationService.EnsureProfileAsync` creates a `Profile` **row** with a relative `DirPath` and never sets `IsActive`. They are never reconciled.

**Net effect:** switching profiles works (real dir-symlink repoint), but activating a preset makes no var loadable in VaM. **The install feature does not function.**

---

## 2. Ground truth — the legacy on-disk contract (must reproduce)

From the live `F:\VaM_1.22.0.3` (do **not** modify it):

```
F:\VaM_1.22.0.3\
├─ AddonPackages  ──▶ (dir symlink) ───┐   ← the ONE link VaM reads; repointed on profile switch
├─ ___addonpacksswitch ___\            │   ← profiles root (literal trailing space before "___")
│   ├─ default\                        │
│   ├─ ng9\  ◀────────────────────────┘   ← active profile
│   │   ├─ ___VarsLink___\                 ← per-var INSTALL links (live: 3,302)
│   │   │   ├─ !AjaX.Arm_Muscles.2.var  ──▶ (file symlink) F:\AddonPackages\___VarTidied___\!AjaX\!AjaX.Arm_Muscles.2.var
│   │   │   └─ … (one file symlink per installed var, named Creator.Package.Version.var)
│   │   └─ ___MissingVarLink___\           ← ALIAS links (missing ref → owned var / 000CanNotFound.Dummy)
│   │       └─ huaQ.QLTS_399.1.var       ──▶ (file symlink) …\000CanNotFound\000CanNotFound.Dummy.1.var
│   └─ … (13 profiles)
└─ AddonPackagesFilePrefs\                 ← hide/fav sidecars (out of scope here; see G5)
```

**Load-bearing invariants (⚠ do not change):**
- Profiles root dir name is **`___AddonPacksSwitch ___`** — verbatim, including the space before the trailing `___`. (VaM/legacy match this literally; `VamProfileService.SwitchDirName` already has it right.)
- `AddonPackages` is a **directory** symlink; the active profile is whatever it resolves to.
- Per-var links are **file** symlinks named exactly `Creator.Package.Version.var` (the identity). VaM keys package identity and dependency resolution off this filename. A numeric or mangled name breaks VaM.
- VaM reads **all** `.var` under `AddonPackages` recursively, so `___VarsLink___\` and `___MissingVarLink___\` subfolders are both picked up.
- Repo files live outside the VaM install (live: `F:\AddonPackages\___VarTidied___\`); links point at the real repo path (VarVault: the hottest online copy's absolute path).

Legacy behaviors worth preserving (from `Form1.VarInstall` etc.):
- **Closure-first**: install a var ⇒ install its full forward-dependency closure too (VarVault already computes this via `IDependencyGraph.ForwardClosureAsync`).
- **Timestamp mirroring**: stamp the link node's times to mirror the source file (`Comm.SetSymboLinkFileTime`) — cosmetic but nice for sort-by-date. *Optional for v1.*
- **`.disabled` marker**: an installed-but-disabled var keeps its link plus a `{name}.disabled` sidecar. *Optional for v1 — model exists room but not required to ship install.*
- **Idempotent re-link on repo relocation** (`FixRebuildLink`): if the repo moves, stale links are deleted and recreated to the current path.

---

## 3. Target design

### 3.1 One reconciled Profile concept
`Profile` row ⇄ on-disk `___AddonPacksSwitch ___\{Name}` directory are the **same thing**, keyed by `Name`.

- `Profile.DirPath` stores the **absolute** path `{vamRoot}\___AddonPacksSwitch ___\{Name}` (resolved from `SettingKeys.VamPath` at build time), OR keep it relative and always combine with `vamRoot` when touching disk. **Decision: store relative (`___AddonPacksSwitch ___/{Name}`) and always resolve against `vamRoot`** — so the DB stays portable if the VaM folder moves. `EfActivationService` gains an `ISettingsService`/`IVamProfileService` dependency to resolve it.
- `Profile.IsActive` is **derived**, not authoritative: set it by reading `VamProfileService.ActiveProfile(vamRoot)` (resolves the `AddonPackages` symlink). Reconcile on load and after every switch.
- `EnsureProfileAsync` must also ensure the on-disk directory exists (`VamProfileService.CreateProfile`) and the `___VarsLink___` / `___MissingVarLink___` subfolders exist.

### 3.2 Activation materializes links on disk
`RecomputeAsync` becomes: compute desired link set → **diff against what's on disk** → create missing symlinks, delete orphaned ones → persist `ActivationLink` rows to match. Idempotent: re-running yields the same on-disk state with no churn.

Per package in the closure:
1. `varFileId = PickHottestOnlineCopyAsync(packageId)` → the source `VarFile` (its absolute path on the repo drive). If none → count `missing`, skip.
2. Compute the **identity filename**: `{Creator}.{Package}.{Version}.var` from the package (not the DB id). Source it from `Package`/`VarFile` identity fields via the fold-safe display identity — see §3.4.
3. `linkPath = {vamRoot}/{Profile.DirPath}/___VarsLink___/{identity}.var`.
4. `targetPath = {absolute path of the source VarFile}`.
5. `symlinks.CreateFile(linkPath, targetPath)` — handle `Result`:
   - success → record/refresh `ActivationLink` row (with real `LinkPath`, `VarFileId`, `LinkKind.Install`, `Reason`).
   - `symlink.privilege` → **abort the whole build** with an actionable error (Developer Mode); do not half-apply. (See §5.1.)
   - `symlink.io` (e.g. link already exists pointing elsewhere) → delete-then-recreate if it's a link we own; else surface conflict.
6. **Orphan sweep**: any `.var` link file in `___VarsLink___\` whose package is no longer in the closure **and** whose `ActivationLink.RequestedByPresetId == presetId` → delete the file + row. Never touch links with `RequestedByPresetId == null` (user-made) — same rule as `RescueAsync`.

### 3.3 Alias links (`___MissingVarLink___`)
For each resolved `VarAlias` in scope (global + this preset), if the missing ref is still not satisfied by a real member, create a **file symlink named after the missing ref** (`{missingRef}.var`) in `___MissingVarLink___\`, pointing at the aliased owned var's source path. Record `ActivationLink { LinkKind = Alias, AliasedMissingRefKey = <missing ref>, Reason = Explicit }`. This reproduces legacy `FormMissingVars.Createlink`. Handle `.latest` → resolve to the highest local version's filename.

### 3.4 Identity filename rule (load-bearing)
The link filename **is** the VaM identity and must be the verbatim `Creator.Package.Version` with the original version token (`.007` ≠ `.7`, per CLAUDE.md). Do **not** derive it from the fold key (fold key is for *matching*, not *display*). Add/confirm a `Package.DisplayIdentity` (or reuse `VarFile.FileName`) that yields exactly `Creator.Package.Version.var`. Prefer the **source `VarFile.FileName`** if it already stores the on-disk name — that's guaranteed VaM-correct. Validate with `ComplyVarName`-equivalent (3 dot-parts, digit version) before writing.

### 3.5 Deactivate / Rescue / Temp on disk
- `DeactivateAsync` and `RescueAsync` must **delete the corresponding link files**, not just rows. Add `ISymlinkService.DeleteLink(path)` (deletes a file *or* directory symlink; refuses non-links — mirror `RepointDirectory`'s guard).
- `CleanTempLinksAsync` deletes `LinkKind.Temp` link files + rows (used by future scene-load temp links; keep the plumbing).

### 3.6 SDK / interface changes
- `ISymlinkService`: add `Result DeleteLink(string linkPath)` and (optional) `Result MirrorTimestamps(string linkPath, string sourcePath)`.
- `IActivationService`: signatures unchanged; behavior now touches disk. `ActivationBuildResult` gains nothing required, but consider `(int LinksCreated, int LinksRemoved, int MissingPackages, int PrivilegeFailures)` for honest UI reporting.
- `EfActivationService` ctor gains `ISettingsService settings` + `IVamProfileService profiles` + `ISymlinkService symlinks`.

---

## 4. GUI wiring (make it reachable)

Currently only profile **Switch** and **Rescue** are wired. Add:

1. **Presets screen — "Activate" action** on a preset → `IActivationService.BuildProfileLinksAsync(presetId)`; report `LinksCreated / MissingPackages / removed` via toast. This ensures the profile's `___VarsLink___` reflects the preset before the user switches to it.
2. **Order of operations surfaced to the user**: (a) pick/create a loading preset, (b) Activate → links materialize in that preset's profile dir, (c) Switch → `AddonPackages` repoints to it, (d) launch VaM. Consider a single "Activate & Switch" convenience.
3. **Deactivate member** in `PresetEditViewModel` → `DeactivateAsync(presetId, packageId)` (ref-counted).
4. **Settings**: ensure `SettingKeys.VamPath` is set before activation is allowed; if unset, disable Activate with the same `profile.novamroot` message `EfProfileService` already returns.
5. **First-run / Developer-Mode check**: if `CreateFile` returns `symlink.privilege`, show the actionable hint (link to enable Developer Mode) — do not fail silently.

---

## 5. Edge cases & safety

| Case | Handling |
|---|---|
| **5.1 No symlink privilege** (Developer Mode off, not elevated) | `CreateFile` → `symlink.privilege`. **Abort build atomically**, report clear hint. Never leave a half-linked profile. |
| **5.2 Repo file offline / removed** | `PickHottestOnlineCopy` returns null → count as missing, skip (no broken link). |
| **5.3 Repo relocated** (link target stale) | Re-running Activate diffs on disk: a link whose target no longer resolves to the current hottest copy is deleted + recreated (`FixRebuildLink` equivalent). |
| **5.4 Link name collides with a user-made file** | If path exists and is **not** a link we own (`RequestedByPresetId == null` or a real file) → refuse, surface conflict; don't clobber. |
| **5.5 Cross-volume** | Symlinks are fine across volumes (unlike legacy `File.Move`). No copy involved. |
| **5.6 Identity filename invalid** (bad var name) | Skip with a logged warning; don't write a malformed link VaM can't parse. |
| **5.7 Concurrent writes** | Link-row persistence goes through `IWriteQueue` (fixes D1 for this path); filesystem ops serialized per-profile via `AsyncLock`. |
| **5.8 `.disabled` markers** | v1: not required. If implemented, a disabled member keeps its row but writes `{name}.disabled` and skips the live link. |
| **5.9 Profile deleted on disk but row exists** | Reconcile on load: mark/prune stale `Profile` rows + their `ActivationLink`s. |

---

## 6. Testing plan (evidence required per [09](09-Implementation-Checklist.md) discipline)

Use `VarVault.TestKit` + the real 277-var corpus at `D:\VarVault_test_repo` (see memory `test-var-repo`). **Never touch `F:\VaM_1.22.0.3`** — tests use a `TempDirectory` fake VaM root.

- **Unit** (`Domain`/`Infrastructure`):
  - `SymlinkService.CreateFile` creates a resolvable file symlink to a target; `DeleteLink` removes the link only, leaves target; refuses non-links. `[Category=Integration]` (touches FS).
  - Identity filename builder yields `Creator.Package.Version.var` incl. verbatim version token (`.007`).
- **Integration** (real SQLite temp + temp VaM root):
  - Activate a preset with N members + deps → exactly the closure count of file symlinks appear under `{profile}\___VarsLink___\`, each named by identity, each resolving to the source. Re-run → idempotent (0 created, 0 removed).
  - Deactivate a member whose dep is shared → shared dep link survives (ref-count); its own link removed.
  - Alias: a missing ref with a `VarAlias` → one link under `___MissingVarLink___\` named after the missing ref, resolving to the aliased var.
  - Rescue → all app-owned links removed, user-made links (null attribution) survive.
  - Privilege failure path: simulate `CreateFile` failure → build aborts, no partial links, actionable error.
- **E2E** (real corpus, headless): index `D:\VarVault_test_repo` → create preset → Activate → assert on-disk links resolve → Switch fake `AddonPackages` → assert it points at the profile. Screenshot/log evidence à la doc 19.

---

## 7. Build checklist

- [ ] **A1** `ISymlinkService.DeleteLink` (+ optional `MirrorTimestamps`); impl + unit tests.
- [ ] **A2** Identity filename source (`VarFile.FileName` or `Package.DisplayIdentity`) confirmed VaM-correct; validator; unit test with verbatim version.
- [ ] **A3** `EfActivationService` gains `ISettingsService` + `IVamProfileService` + `ISymlinkService`; resolve `vamRoot`; `EnsureProfileAsync` creates on-disk profile + `___VarsLink___`/`___MissingVarLink___`.
- [ ] **A4** `RecomputeAsync` materializes install links (create/diff/orphan-sweep), correct `LinkPath` + filename; writes go through `IWriteQueue`. Integration test (idempotent).
- [ ] **A5** Alias links materialized under `___MissingVarLink___` (incl. `.latest`). Integration test.
- [ ] **A6** `DeactivateAsync` / `RescueAsync` / `CleanTempLinksAsync` delete link files (ref-counted). Integration tests.
- [ ] **A7** Profile reconciliation: `IsActive` derived; stale profile/link prune on load.
- [ ] **A8** GUI: Presets "Activate" (+ optional "Activate & Switch"); PresetEdit "Deactivate member"; VaM-path guard; Developer-Mode error surfaced.
- [ ] **A9** Settings guard: Activate disabled until `vam.path` set.
- [ ] **A10** E2E on real corpus with a temp VaM root; evidence logged.
- [ ] **A11** Update [10-Decisions-Log](10-Decisions-Log.md): supersede/clarify **BE-G1** — activation now materializes per-var links (the "no per-var Library buttons" UI choice may stand, but the engine is no longer a no-op).

---

## 8. Out of scope for this spec (tracked elsewhere)
- Usage feed / VaM-log import (**G1** — separate spec; the bigger P0).
- `.disabled` enable/disable UX, timestamp mirroring polish (nice-to-have; noted in §2/§5.8).
- Temp links for live scene-loading (**load-into-VaM**, sealed out of v1) — plumbing kept, no producer.
- Hide/fav curation (**G5**), unwired engines (**G4**), CLI (**G3**) — their own follow-ups.

---

*The §7 A1–A11 items are decomposed into granular, evidence-gated build tasks (T1.1–T7.5) in **[22 — Activation & Symlink-Install Implementation Checklist](22-Activation-Symlink-Install-Implementation-Checklist.md)** — build from that. Once green, G2 in [20-Legacy-Feature-Gap-Analysis](20-Legacy-Feature-Gap-Analysis.md) flips from "install feature is a filesystem no-op" to "install materializes VaM-correct symlinks." Next spec: **23 — Usage Feed & VaM-Log Import (fix G1).***
