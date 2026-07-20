# 22 — Activation & Symlink-Install: Implementation Checklist

**Implements:** [21-Activation-Symlink-Install-Spec](21-Activation-Symlink-Install-Spec.md) (fixes [20](20-Legacy-Feature-Gap-Analysis.md) **G2** + **D2**, partially **D1**)
**Date:** 2026-07-20

---

## ✅ Implementation status (2026-07-20) — **G2 fixed**

Preset activation now **materializes real per-var file symlinks on disk** (install links under `___VarsLink___`, alias links under `___MissingVarLink___`), with `ActivationLink` rows mirroring the filesystem. The three original defects are gone: (1) `SymlinkService.CreateFile` is actually called; (2) link filenames are the verbatim `Creator.Package.Version.var` identity (no numeric id); (3) `vamRoot` is resolved from `SettingKeys.VamPath`.

**Done:** P1 (DeleteLink) · P2 (ActivationPaths) · P3 (Profile reconcile + VaM root) · P4 (materialize install links, diff/orphan-sweep, atomic privilege abort, widened `ActivationBuildResult`) · P5 (alias links + disk-level deactivate/rescue/temp) · P6 (Presets Activate / Activate&Switch + VaM-path guard + Dev-Mode hint; Settings Browse + VaM validation; dead symlink dropdown removed; reconcile-on-startup).

**Evidence:**
- **Real-data E2E on the 277-var corpus** (via the real composition root): indexed 277 vars → preset of 5 real members → **8 links** (5 members + 3 real deps via closure); every link named `Creator.Package.Version.var`, every target a real corpus file, all under the profile's `___VarsLink___`; idempotent rebuild (0 churn); orphan sweep on deactivate. *(harness in scratchpad — no machine path committed; corpus path passed as arg.)*
- xUnit (last clean build): `ActivationFlowTests` (6) + `ActivationEdgeCasesTests` privilege-abort/reconcile/parallel (3) green; `SymlinkServiceTests` incl. real `DeleteLink` (7), `ActivationPathsTests` (5), `VamProfileServiceTests` green. New privilege-independent `Activation_calls_CreateFile_with_identity_filenames_and_real_targets` added (recording-fake — verifies the create path in any environment).
- `src/` builds clean (0 errors). **No hardcoded absolute paths** in production code (verified).

### Operational requirement: symlink privilege — **handled via elevation manifest**
Creating symlinks on Windows needs **Developer Mode enabled OR the app run elevated** — else `File.CreateSymbolicLink` fails with `ERROR_PRIVILEGE_NOT_HELD`. Handled gracefully in code: `SymlinkService` returns `symlink.privilege` with an actionable hint, `RecomputeAsync` aborts **atomically** (0 partial links), and `PresetsViewModel` surfaces the Developer-Mode hint.

**Resolution:** [`src/VarVault.App/app.manifest`](../../src/VarVault.App/app.manifest) now ships `<requestedExecutionLevel level="requireAdministrator" />` — the app auto-elevates on launch (a UAC prompt), so symlinks work out of the box without the user enabling Developer Mode. **This matches legacy varManager**, which ships the identical `requireAdministrator` manifest (and is why it "launches as admin"). Verified: `requireAdministrator` is embedded in the built `VarVault.App.exe`. Trade-off: UAC prompt every launch. Lighter-touch alternatives (documented in the manifest comment): `highestAvailable` (elevate only for admin users), or enable Developer Mode once and drop to `asInvoker`.

**Proven end-to-end on a real install (2026-07-20):** against `F:\VaM_1.20.77.9` (run elevated), VarVault created a real preset `VarVault_Demo`, materialized **13 real NTFS symlinks** (8 members + 5 deps) into `___AddonPacksSwitch ___\VarVault_Demo\___VarsLink___`, then **switched** `AddonPackages` → that profile — VaM sees all 13 vars through the chain (valid `PK` zips). Existing profiles (`nw5`, …) untouched. This reproduces legacy varManager's exact install+switch model.

---

**Rule:** an item is `[x]` **only** with concrete evidence — a passing test method name, a benchmark, or run output (per [09](09-Implementation-Checklist.md)/[14](14-Testing-and-Observability-Standards.md) discipline). Tests that create real symlinks are `[Category=Integration|E2E]` and need Windows Developer Mode (CI already runs `SymlinkServiceTests`/`VamProfileServiceTests`, so the environment supports it).

**Golden facts (verified in source — build against these, don't re-derive):**
- Source var absolute path = `Path.Combine(Repository.MountPath, VarFile.RelativePath)`. `Repository.Id` is a `Guid`; `VarFile.RepositoryId` is `Guid`.
- VaM identity filename = **`Package.VarName`** (verbatim `Creator.Package.Version`, e.g. `.007` preserved) → link file `"{Package.VarName}.var"`. **No new filename builder needed** — never use the numeric `PackageId`, never use `IdentityKey` (fold key is match-only).
- Profiles root dir = **`___AddonPacksSwitch ___`** (verbatim, trailing space) — already `VamProfileService.SwitchDirName`. Install links go in `{vamRoot}\___AddonPacksSwitch ___\{Profile.Name}\___VarsLink___\`; alias links in `…\___MissingVarLink___\`. Both flat (no per-creator subfolder), matching the live install.
- VaM root comes from `settings.GetAsync(SettingKeys.VamPath)` (`"vam.path"`).
- `ActivationLink` already has every field needed (`LinkPath`, `LinkKind` Install/Alias/Temp, `AliasedMissingRefKey`, `LinkSubfolder`, `LinkType`, `Reason`, `RequestedByPresetId`, `VarFileId`, `ProfileId`) — **no schema/migration change required** unless we add `Package.DisplayIdentity` (we don't — use `VarName`).
- DI today: `ISymlinkService`→`SymlinkService`, `IVamProfileService`→`VamProfileService` (singletons, `InfrastructureRegistration.cs:30-31`); `IActivationService`→`EfActivationService`, `IProfileService`→`EfProfileService` (scoped, `PersistenceRegistration.cs:59,70`).
- Writes should route through `IWriteQueue.EnqueueAsync(..., WritePriority.Interactive)` (fixes D1 for this path).

---

## Phase 1 — Symlink primitive: delete + resolve (foundation)

- [ ] **T1.1** Add `Result DeleteLink(string linkPath)` to `ISymlinkService` ([Domain/Activation/ISymlinkService.cs](../../src/VarVault.Domain/Activation/ISymlinkService.cs)). Deletes a **file or directory** symlink; refuses a non-link (`symlink.notalink`) and a missing path is success (idempotent). Mirror the `IsLink` guard from `RepointDirectory`.
  - *Evidence:* `SymlinkServiceTests.DeleteLink_removes_file_symlink_leaves_target`, `…_refuses_non_link`, `…_missing_path_is_success`.
- [ ] **T1.2** Impl in [Infrastructure/Activation/SymlinkService.cs](../../src/VarVault.Infrastructure/Activation/SymlinkService.cs): `File.Delete`/`Directory.Delete` chosen by attributes; reuse `Classify(ex)`.
  - *Evidence:* same tests green (`[Category=Integration]`).
- [ ] **T1.3** *(optional, defer-ok)* `Result MirrorTimestamps(string linkPath, string sourcePath)` — set link node create/write times to the source's (legacy `Comm.SetSymboLinkFileTime`, cosmetic sort-by-date). Skip for v1 unless cheap.
  - *Evidence:* `SymlinkServiceTests.MirrorTimestamps_copies_source_times` or explicitly deferred in this doc.

## Phase 2 — Identity filename + source-path resolution

- [ ] **T2.1** Add a small pure helper `ActivationPaths` (Domain/Activation) or private methods: `LinkFileName(Package) => $"{VarName}.var"` and `SourcePath(Repository, VarFile) => Path.Combine(MountPath, RelativePath)`. Validate the var name (3 dot-parts, digit version) before use; invalid → skip + log (spec §5.6).
  - *Evidence:* `ActivationPathsTests.LinkFileName_is_verbatim_varname_with_007_preserved`, `…_source_path_combines_mount_and_relative`.
- [ ] **T2.2** Rework `EfActivationService.PickHottestOnlineCopyAsync` to project everything the link needs in one query: `(long VarFileId, string VarName, string MountPath, string RelativePath)` — join `VarFiles`→`Packages`→`Repositories`, `where IsOnline && IsEnabled`, `orderby r.Tier, r.PriorityInTier, v.Id`. Returns null when no online copy (→ `missing++`).
  - *Evidence:* covered by the Phase 4 idempotency/closure tests.

## Phase 3 — Reconcile Profile (row ⇄ on-disk dir) + VaM root

- [ ] **T3.1** Inject `ISettingsService settings`, `IVamProfileService profiles`, `ISymlinkService symlinks` into `EfActivationService` ctor. Add `private async Task<Result<string>> VamRootAsync(ct)` mirroring `EfProfileService.VamRootAsync` (fail `profile.novamroot` if unset).
  - *Evidence:* `ActivationFlowTests` updated to seed `SettingKeys.VamPath` = a `TempDirectory` (see T7.1).
- [ ] **T3.2** `EnsureProfileAsync`: (a) create/find the `Profile` row keyed by `preset.Name`; (b) store `DirPath` **relative** = `"___AddonPacksSwitch ___/{Name}"`; (c) create the on-disk profile dir + `___VarsLink___` + `___MissingVarLink___` subdirs under `vamRoot` (via `Directory.CreateDirectory` / `profiles.CreateProfile`). Idempotent.
  - *Evidence:* `ActivationFlowTests.Activate_creates_profile_dirs_on_disk`.
- [ ] **T3.3** Derive `Profile.IsActive` from `profiles.ActiveProfile(vamRoot)` on build + expose a `ReconcileProfilesAsync` used at startup to prune `Profile` rows whose on-disk dir vanished (+ their `ActivationLink`s). Wire the prune into app startup (`AppHost`) or the index orchestrator.
  - *Evidence:* `ActivationFlowTests.Stale_profile_row_pruned_when_dir_missing`; `IsActive` reflects the switched profile.

## Phase 4 — Materialize INSTALL links (the core fix)

- [ ] **T4.1** Rewrite `RecomputeAsync` to a **desired-vs-disk diff**, not blind row insert:
  1. Resolve `vamRoot`; fail fast if unset.
  2. Compute `full` closure (existing `ForwardClosureAsync` per direct member — keep).
  3. Build desired map `{ packageId → (VarFileId, linkFileName, sourcePath) }` via T2.2 (skip missing).
  4. `varsLinkDir = {vamRoot}/___AddonPacksSwitch ___/{Name}/___VarsLink___`.
  5. **Create**: for each desired link not already a correct symlink on disk → `symlinks.CreateFile(linkPath, sourcePath)`. If exists but points elsewhere and it's app-owned → `DeleteLink` then recreate (`FixRebuildLink` equiv, spec §5.3).
  6. **Orphan-sweep**: for each existing `ActivationLink` with `RequestedByPresetId == presetId` (or on-disk `.var` link) whose package left the closure → `DeleteLink` + remove row. **Never** touch `RequestedByPresetId == null` (user-made).
  7. Persist rows to mirror disk, through `IWriteQueue` (Interactive). `LinkPath` = the **real absolute** link path; `LinkKind.Install`; `Reason` = Explicit for members else DependencyOf.
  8. Return `ActivationBuildResult` (see T4.3).
  - *Evidence:* `ActivationFlowTests.Activate_materializes_symlinks_named_by_identity_resolving_to_source` (assert files exist under `___VarsLink___`, each `IsLink`, `ResolveTarget`→source, filename == `{VarName}.var`).
- [ ] **T4.2** Idempotency + ref-counting on disk: re-Activate → 0 created / 0 removed, same files; deactivate a member sharing a dep → shared link survives, member link removed.
  - *Evidence:* extend existing `ActivationFlowTests` rebuild-not-duplicated (line 76) and ref-count (line 109) tests to also assert **file** presence/absence, not just row counts.
- [ ] **T4.3** Widen `ActivationBuildResult` → `(int LinksCreated, int LinksRemoved, int MissingPackages, int PrivilegeFailures)` for honest UI. Update `IActivationService` XML doc + all callers/tests.
  - *Evidence:* compile + tests reference new fields.
- [ ] **T4.4** Privilege failure = **atomic abort**: first `CreateFile` returning `symlink.privilege` aborts the build (roll back rows created this call; leave prior state), surfaced via result/error. No half-linked profile.
  - *Evidence:* `ActivationFlowTests.Privilege_failure_aborts_without_partial_links` (inject a fake `ISymlinkService` returning `symlink.privilege`).

## Phase 5 — Alias links + deactivate/rescue/temp on disk

- [ ] **T5.1** Materialize **alias** links: for each in-scope resolved `VarAlias` whose missing ref isn't satisfied by a real member, `CreateFile("{missingRef}.var" in ___MissingVarLink___, aliasTargetSourcePath)`; row `LinkKind.Alias`, `AliasedMissingRefKey = missingRef`. Handle `.latest` → highest local version's `VarName`.
  - *Evidence:* `ActivationFlowTests` alias test (line ~209) extended to assert an on-disk file under `___MissingVarLink___` resolving to the target.
- [ ] **T5.2** `DeactivateAsync`: after recomputing the reduced set, **delete the removed link files** (not just rows) via the T4.1 orphan-sweep path.
  - *Evidence:* `ActivationFlowTests.Deactivate_removes_only_that_members_link_files`.
- [ ] **T5.3** `RescueAsync`: delete **all app-owned link files** in the profile (`RequestedByPresetId != null`) then the rows; user-made links (null attribution) survive on disk + DB.
  - *Evidence:* extend rescue test (line ~178) to assert files gone but the user link file remains.
- [ ] **T5.4** `CleanTempLinksAsync`: delete `LinkKind.Temp` link files + rows.
  - *Evidence:* extend temp test (line ~174) to assert file deletion.

## Phase 6 — GUI wiring + settings guard (make it reachable)

- [ ] **T6.1** Presets screen: add **Activate** command → `IActivationService.BuildProfileLinksAsync(preset.Id)`; toast `"{LinksCreated} linked · {MissingPackages} missing · {LinksRemoved} removed"`. Add optional **Activate & Switch** (Activate then `profiles.SwitchToAsync`). Files: `ViewModels/PresetsViewModel.cs`, `Views/PresetsView.axaml`.
  - *Evidence:* `PresetsViewModelTests.Activate_reports_counts`; manual/E2E run screenshot.
- [ ] **T6.2** PresetEdit: add **Deactivate member** → `DeactivateAsync(preset.Id, packageId)`; refresh closure preview. Files: `ViewModels/PresetEditViewModel.cs`, `Views/PresetEditDialog.axaml`.
  - *Evidence:* `PresetEditViewModelTests.Deactivate_member_drops_link`.
- [ ] **T6.3** Settings guard: Activate/Deactivate disabled (with tooltip `profile.novamroot` message) until `SettingKeys.VamPath` is set + points at a folder containing `VaM.exe`/`AddonPackages`. Files: `ViewModels/SettingsViewModel.cs` (validation), `PresetsViewModel` (CanActivate).
  - *Evidence:* `PresetsViewModelTests.Activate_disabled_without_vam_path`.
- [ ] **T6.3a** VaM-path setting UX (setting **already exists** — General tab, `PART_VamPath` bound to `VamPath`/`SettingKeys.VamPath`; this task hardens it, doesn't add it):
  - Add a **Browse** button (Avalonia `StorageProvider.OpenFolderPickerAsync`) beside `PART_VamPath` → sets `VamPath`. Files: `Views/SettingsView.axaml`, `ViewModels/SettingsViewModel.cs`.
  - Add **inline validation**: the chosen folder must contain `VaM.exe`, `AddonPackages`, or `___AddonPacksSwitch ___` (accepts vanilla *and* already-managed installs; the live install has `Qvaro.exe`+`AddonPackages`, not `VaM.exe`). Invalid → red hint under the field. Save still **persists** the path (never lose input) but appends a warning; `EfActivationService` guards defensively (skips activation if the folder doesn't exist) rather than blocking the whole settings save. Expose `IsVamPathValid`.
  - *Evidence:* `SettingsViewModelTests.VamPath_valid_only_when_folder_has_vam_exe`; manual Browse screenshot.
- [ ] **T6.3b** Resolve the **dead "Symlink strategy" dropdown**: `SymlinkOptions` (`"Directory-swap profiles (fast)"` / `"Per-var symlinks"`) is saved to `symlink.type` but **no code reads it** (verified) — it misleadingly implies a toggle next to the feature we're building. Choose one:
    - **(a) Remove it** — the two mechanisms are not either/or (profile switch *and* per-var links both always apply; §2). Simplest, recommended.
    - **(b) Repurpose** as `LinkType` Symlink-vs-Hardlink (the `ActivationLink.LinkType` enum exists) *only if* hardlink support is actually planned — otherwise (a).
  - Files: `Views/SettingsView.axaml`, `ViewModels/SettingsViewModel.cs` (drop `SymlinkOptions`/`SymlinkType`/`SymlinkKey` for (a)).
  - *Evidence:* dropdown gone (or wired to a real reader) — no orphaned `symlink.type` write.
- [ ] **T6.4** Developer-Mode error surfaced: on `symlink.privilege`, show the actionable `SymlinkService.DevModeHint` (link to enable Developer Mode) via toast/dialog — never fail silently. Files: `PresetsViewModel`, `Services/DialogService`.
  - *Evidence:* manual verification note + unit test that the privilege error message propagates to `StatusMessage`.

## Phase 7 — Tests, E2E, docs

- [ ] **T7.1** Update `ActivationFlowTests` setup to provision a **temp VaM root** (`TempDirectory`) via `SettingKeys.VamPath`, so all existing row-level assertions keep passing *and* gain file-level assertions. **Never** touch `F:\VaM_1.22.0.3`.
  - *Evidence:* full `ActivationFlowTests` class green.
- [ ] **T7.2** New E2E: index the real corpus `D:\VarVault_test_repo` (memory `test-var-repo`, 277 vars) → create preset with a few members → Activate → assert on-disk `___VarsLink___` links resolve to real corpus files → Switch fake `AddonPackages` → assert it resolves to the profile. Headless, evidence logged à la doc 19.
  - *Evidence:* `ActivationFlowTests` (E2E) or a new `RealRepoActivationE2E` with run output.
- [ ] **T7.3** Concurrency: link-row writes go through `IWriteQueue`; per-profile filesystem ops guarded by `AsyncLock`. Assert no interleave corruption under a parallel Activate/Deactivate.
  - *Evidence:* `ActivationConcurrencyTests.Parallel_activate_deactivate_is_consistent`.
- [ ] **T7.4** Update [10-Decisions-Log](10-Decisions-Log.md): clarify/supersede **BE-G1** — the activation engine now materializes per-var links on disk; the "no per-var buttons on Library" UI choice may stand, but activation is no longer a filesystem no-op.
  - *Evidence:* doc 10 diff.
- [ ] **T7.5** Flip **G2** in [20-Legacy-Feature-Gap-Analysis](20-Legacy-Feature-Gap-Analysis.md) from "install feature is a filesystem no-op" to resolved, with the passing test names as evidence.
  - *Evidence:* doc 20 diff + green suite.

---

## Edge-case coverage matrix (must each have a test or explicit deferral)

| # | Case (spec §5) | Covered by |
|---|---|---|
| 1 | No symlink privilege → atomic abort | T4.4 |
| 2 | Repo file offline/removed → skip, no broken link | T4.1 (missing count) |
| 3 | Repo relocated → stale link recreated | T4.1 step 5 + a targeted test |
| 4 | Name collides with user-made file → refuse, don't clobber | T4.1 step 6 / T5.3 |
| 5 | Cross-volume | inherent (symlink, no copy) — note only |
| 6 | Invalid identity filename → skip+log | T2.1 |
| 7 | Concurrent writes | T7.3 |
| 8 | `.disabled` markers | **deferred v1** — note in this doc |
| 9 | Profile dir vanished but row exists | T3.3 |

---

## Suggested build order (dependency-respecting)
**P1 → P2 → P3 → P4 → P5 → P6 → P7.** P1–P2 are pure/low-risk and unblock everything. P4 is the value delivery — after P4 the install feature *works* headlessly (provable via T4.1/T7.1) even before the GUI (P6). Ship P1–P4 as the first vertical slice; P5–P6 as the second; P7 rides along throughout.

## Out of scope (own follow-ups)
`.disabled` enable/disable UX · timestamp mirroring polish (T1.3) · temp links for live scene-load (sealed out of v1) · usage feed (**G1** → spec 23) · hide/fav (**G5**) · other unwired engines (**G4**) · CLI (**G3**).
