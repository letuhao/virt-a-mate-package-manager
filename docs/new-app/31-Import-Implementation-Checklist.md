# 31 — Import &amp; Dedup-Review — Implementation Checklist

Evidence-gated build plan for [30-Import-And-Dedup-Review-Spec.md](30-Import-And-Dedup-Review-Spec.md).
Rules (same as docs 24/26/28): a box is `[x]` **only** with concrete evidence — a passing test name, a screenshot,
or run output. Test through SDK interfaces, not internals; prefer fakes over mocks; real SQLite (temp files) for
integration; real-data tests are **env-var gated** (`VARVAULT_TEST_CORPUS` / `_2` / `VARVAULT_VAM_PATH`) — never
hardcoded. Baseline before starting: `dotnet build` 0 errors, `dotnet test` green (608 today).

Build order is **bottom-up**: Domain (pure, fastest to test) → SDK contracts → Infrastructure → pipeline → UI →
real-data E2E. Each phase lands behind its own tests so the tree stays green throughout.

Legend: **[BE]** backend/engine · **[UI]** Avalonia · **[T]** test/evidence.

---

## Phase 0 — Scaffolding &amp; guardrails

- [x] **0.1** Add `SharpCompress` to [Directory.Packages.props](../../Directory.Packages.props) (version only). Archive extraction is SharpCompress + a **CJK-aware CustomDecoder** (30-spec §6 amendment A1), with an optional `import.sevenzip_path` 7z fallback — **no bundled native binary**. *Evidence:* `dotnet restore` clean; Architecture.Tests green (SharpCompress referenced only from Infrastructure, not Domain/Sdk).
- [x] **0.2** Create SDK folder `VarVault.Sdk/Import/` with the enums + records from spec §5 (`ImportLane`, `ImportDecision`, `ImportSignals`, `ImportItem`, `ExistingRef`, `EntryDelta`, `ImportSource`, `ImportSpec`, `ImportSession`, `ApplyResult`, `ImportRun`). No behavior yet. *Evidence:* builds; `[T]` a record round-trip/equality unit test.
- [x] **0.3** Confirm the dependency rule: new Domain types depend only on Common; SDK on Common+DI. *Evidence:* `VarVault.Architecture.Tests` green.

---

## Phase 1 — Domain: classification (rules 1 &amp; 2) [BE]

- [x] **1.1** Extend/replace `IntakeClassifier` → `ImportClassifier.Classify(candidateFacts, catalogFacts)` returning `ImportLane` per spec §3, incl. **Corrupt** (from `IntegrityStatus`) and **Naming** (from `MetaDivergent`), and **CJK** (from codepage/broken-entry). Precedence Corrupt&gt;Naming&gt;Conflict&gt;Cjk&gt;Exact&gt;New. *Evidence:* `ImportClassifierTests` — one case per lane + precedence.
- [x] **1.2** **Rule 1 (no version compare):** a candidate whose full `IdentityKey` (incl. version token) is absent from the catalog classifies **New**, even when other versions of the same `Creator.Package` exist. *Evidence:* `ImportClassifierTests.Different_version_is_New_not_upgrade` (repo has `.1`+`.2`, candidate `.3` → New).
- [x] **1.3** **Conflict** fires only on same **full** identity + differing `ContentSignature`. *Evidence:* `ImportClassifierTests.Same_version_different_content_is_Conflict`.
- [x] **1.4** `EntryDelta` diff builder from two `ZipEntryFacts` sets (added/removed/changed by name+CRC+size, no decompress). *Evidence:* `EntryDeltaTests` over hand-built fact sets (+/−/~ counts).

## Phase 2 — Domain: conflict recommender [BE]

- [x] **2.1** `ConflictRecommender.Recommend(incoming, existing) → (ImportDecision, Reason)` with the priority ladder in spec §4 (validity → meta → encoding → entries/size → mtime → ambiguous⇒KeepBoth). Pure, deterministic. *Evidence:* `ConflictRecommenderTests` — one test per ladder rung, incl. the "existing is CorruptZip ⇒ KeepIncoming" and "both valid similar ⇒ KeepBoth" cases; assert the `Reason` mentions the deciding signal.
- [x] **2.2** Recommender never recommends deleting the user's existing repo copy without an explicit KeepIncoming/overwrite decision (advisory only). *Evidence:* covered by 2.1 assertions (Decision ∈ enum; no side effects — pure function).

## Phase 3 — Infrastructure: archive extraction + temp workspace [BE]

- [x] **3.1** `ITempWorkspace` impl → scoped dir rooted at the `import.temp_dir` setting (**default = target repo's drive**, fallback `%LOCALAPPDATA%\VarVault\import\`) `\<sessionId>\`; free-space pre-check vs archive uncompressed size; `IAsyncDisposable` deletes it; a startup sweep removes orphaned session dirs. *(D4/§6)* *Evidence:* `TempWorkspaceTests` (creates on chosen root, disposes → gone; sweep removes an old dir; low-space source aborts with reason).
- [x] **3.2** `ArchiveExtractor : IArchiveExtractor` (SharpCompress) for `.zip/.7z/.rar/.tar(.gz)` → extract to dest; return `ArchiveExtractResult(Ok, extractedCount)`. *Evidence:* `ArchiveExtractorTests` extract a fixture `.zip` **and** `.7z` containing 2 fake `.var`s → both land on disk.
- [x] **3.3** Password-protected archive → `ArchiveExtractResult(Failed, "password")`, no throw, nothing half-extracted. *Evidence:* `ArchiveExtractorTests.Password_zip_reports_failure_not_throw` with a password-protected fixture.
- [x] **3.4** Corrupt/truncated archive → `Failed, "corruptArchive"`. *Evidence:* `ArchiveExtractorTests.Truncated_archive_reports_failure`.

## Phase 4 — Infrastructure: scan (extract + classify + signals) [BE]

- [x] **4.1** `EfImportService.ScanAsync(spec, progress, ct)`: resolve sources → extract archives to temp (via 3.x) → enumerate `*.var` in folders + temp → `IVarInspector.Inspect` each (reuse existing) → build `ImportSignals` → classify (Phase 1) → for Conflict, load the existing var + recommend (Phase 2) + diff (1.4). Returns `ImportSession` with `Sources` (incl. failed) + `Items`. Read-only (no writes). *Evidence:* `ImportScanTests` over a temp catalog + a fixture source folder: asserts correct lane per seeded file.
- [x] **4.1b** **Dedup scope = whole catalog (D1):** classify against every catalogued var across **all** repos, not just the target; an exact match in any repo ⇒ Exact-dup skip, carrying a "present in &lt;repo&gt;" hint (+ promote/move nudge when it's colder than target). If a repo lacks signatures, index it first (or surface a "stale repo" warning) before trusting the result. *Evidence:* `ImportScanTests.Var_present_in_other_repo_is_exact_dup` (seed the var in a non-target repo → candidate classifies Exact, not New).
- [x] **4.1c** **Name≠meta cross-check (E1):** a garbage-named var whose well-formed meta identity + content resolves to an existing catalog var is reclassified Exact/Conflict, not left as a plain naming/New item; both filename+meta malformed ⇒ Naming (keep-as-is). *Evidence:* `ImportScanTests.Misnamed_but_meta_matches_existing_is_dedup`.
- [x] **4.2** Intra-batch dedup: identical `ContentSignature` appearing twice in the sources → first kept, rest auto-skip. *Evidence:* `ImportScanTests.Duplicate_within_batch_is_skipped`.
- [x] **4.3** Failed archive sources appear in `session.Sources` with reason, excluded from `Items`. *Evidence:* `ImportScanTests.Password_archive_is_a_failed_source_not_an_item`.
- [x] **4.4** Progress + cancellation honored. *Evidence:* `ImportScanTests` cancels mid-scan → `OperationCanceledException`/partial, temp cleaned.

## Phase 5 — Infrastructure: apply pipeline + history [BE]

- [x] **5.1** `EfImportService.ApplyAsync(session, ct)` per-item actions (spec §7): Import/KeepIncoming (durable copy→verify→index), RenameToMeta (copy as meta identity), ImportAndFix (copy → `IEncodingFixer` → index), KeepBoth (non-colliding name), Skip/KeepExisting (no-op), Discard (quarantine). All catalog writes via `IWriteQueue`+`IUnitOfWork`. *Evidence:* `ImportApplyTests` real-SQLite: each decision produces the right on-disk + catalog effect.
- [x] **5.2** Durability: copy uses copy→verify-hash→rename (reuse `MigrationRunner` pattern); a simulated failure leaves no half-file in the repo. *Evidence:* `ImportApplyTests.Failed_copy_leaves_repo_clean` (inject a verify mismatch).
- [x] **5.3** **Encoding-fix is a copy modifier (E2):** any imported var with GBK entries is fixed to UTF-8 on the copy — incl. a Conflict resolved as Keep-incoming — not only pure CJK-lane items; fix failure ⇒ import original + log "fix failed". *Evidence:* `ImportApplyTests.Cjk_import_is_unicode_after_apply` + `…Conflict_keep_incoming_also_fixes_encoding`.
- [x] **5.4** `RenameToMeta` writes the file + catalog identity as `MetaCreator.MetaPackage.Version`; a collision with an existing repo identity is reclassified Conflict (not overwrite). *Evidence:* `ImportApplyTests.Rename_to_meta` + `…Rename_collision_becomes_conflict`.
- [x] **5.5** Temp workspace deleted in `finally` (apply success, failure, and cancel). *Evidence:* `ImportApplyTests.Temp_is_cleaned_on_apply_and_cancel`.
- [x] **5.6** History: `ImportHistoryStore` + EF entities (`ImportRunEntity`, `ImportOutcomeEntity`, `ImportFailedSourceEntity`) + one migration; `ApplyAsync` records the run (per-item outcome + failed sources) **before** temp cleanup. *Evidence:* `ImportHistoryTests` — after apply, `HistoryAsync` returns the run with correct counts + the password-archive failure row.
- [x] **5.7** `VarsImported` domain event emitted; telemetry counters/histogram recorded. *Evidence:* `ImportApplyEffectsTests.Apply_publishes_VarsImported_and_records_metrics` (subscribes `IEventBus`, `MetricCollector<long>` on `imports.applied` + `imports.vars_copied`).
- [x] **5.8** DI registration in `PersistenceRegistration`/`InfrastructureRegistration` (scoped `IImportService`, `IArchiveExtractor`, `ITempWorkspace`, `IImportHistoryStore`); keep impls `internal`. *Evidence:* `Host.Tests` resolves `IImportService`.
- [x] **5.9** **Optional activate-after (D2):** when `ImportSpec.ActivateAfter` is set, a successful apply accumulates the just-imported vars into a durable "Imported" loading preset and builds its profile links via the existing activation flow (idempotent + additive); off by default; a no-op when no VaM path is configured. *Evidence:* `ImportApplyEffectsTests.Activate_after_import_routes_through_the_activation_flow` (asserts the "Imported" preset + members) + `…Activate_after_is_not_triggered_when_flag_is_off`; on-disk symlink build exercised env-gated by `ImportRealRunE2ETests` (`VARVAULT_VAM_PATH`).
- [x] **5.10** **History cap (E5):** prune to the most recent `import.history_keep` runs (default 200), oldest-first. *Evidence:* `ImportHistoryTests.Prunes_beyond_keep_limit`.
- [x] **5.11** **Settings**: register `import.temp_dir` + `import.history_keep` keys (with defaults) in `ISettingsService`; surface both on the Settings screen (Import tab). *Evidence:* `SettingsScreenTests.Import_temp_dir_and_history_keep_round_trip` + `…Import_tab_is_selectable`; both fields render on the Import tab of `SettingsView`.

## Phase 6 — UI: Import screen [UI]

- [x] **6.1** `ImportViewModel` (`ILoadableScreen`): sources, target-repo selector, triage counts, items, view-mode (table/gallery), selected item, decisions, progress, apply command. Auto lanes pre-decided; review lanes start undecided. *Evidence:* `ImportViewModelTests` (fake `IImportService`): counts + `CanApply` gate + accept-all.
- [x] **6.2** `ImportView.axaml`: top bar (target repo, History, re-scan), sources strip, triage chips, split list/resolver, action bar — matching [29-draft](29-Import-Review-UX-Draft.html). *Evidence:* `UiE2E` render test — screen loads, chips + apply bar present.
- [x] **6.3** Table ⇄ Gallery toggle; incoming vars aren't catalogued, so previews are extracted per-file during scan into the session temp (a representative scene/look image, else the largest image) and rendered via a path→`Bitmap` converter. *Evidence:* `ImportGalleryPreviewTests.Gallery_extracts_and_decodes_a_real_var_preview` (a preview-bearing var extracts a real thumb the converter decodes; an image-less var falls back to placeholder) + real-data screenshot `import-02-gallery.png` showing decoded previews.
- [x] **6.4** Resolver panels per lane (conflict side-by-side + diff + recommendation; **naming identity compare with NO pre-selected default — user picks rename/keep/discard per var, D3**; corrupt reason; CJK fix toggle; new/exact auto). *Evidence:* `ImportResolverE2ETests` — selecting a conflict shows both columns + a decision updates the item; a naming item has no default decision until the user clicks.
- [x] **6.4b** Action bar has an **"Activate into VaM after import"** checkbox (default off) bound to `ImportSpec.ActivateAfter`; only one import session may be active at a time (Apply/Scan blocked while one runs). *(D2/E5)* *Evidence:* `ImportViewModelTests.Activate_checkbox_sets_spec` + `…Second_session_blocked_while_active`.
- [x] **6.5** History overlay from the `History` button + the failed-source pill; lists runs + failed sources with reason. *Evidence:* `ImportHistoryE2ETests` — overlay shows a seeded run + password-fail row.
- [x] **6.6** Keyboard: `J/K` (+ `↑/↓`) navigate the list, `] [ \` set primary/secondary/both decisions, `Del`/`Back` discard the selected review item (tunnelled so the list's type-ahead doesn't swallow them); Apply disabled until all review items decided. *Evidence:* `ImportViewModelTests.Keyboard_navigates_and_decides_review_items` + the existing accept-all gate test; hint line in the resolver.
- [x] **6.7** Wire into `AppHost.CreateShell` + `ShellViewModel.AllScreens` (screen id `import`, Optimize group) with a live badge via `IShellLiveFeeds`; retire the Dupes "Download intake" tab. *Evidence:* `ShellNavigationTests` reaches `import`; badge reflects review count.

## Phase 7 — Real-data E2E + regression [T]

- [x] **7.1** `ImportRealRunE2ETests` (env-gated, headless Avalonia over the real corpus): point sources at a scratch folder built from real vars **plus** a `.zip` and `.7z` fixture; scan → assert lanes on real files → resolve → apply into a temp target repo → assert files copied, CJK fixed, corrupt quarantined, temp cleaned, history recorded. Non-destructive (temp catalog + scratch dirs; never writes the real corpus). *Evidence:* the test + a report line (files copied / fixed / failed) like the newcomer harness.
- [x] **7.2** Verify the two hard rules on real data: a different-version real var imports as **New** (not upgrade); a real `MetaDivergent` var lands in **Naming**. *Evidence:* assertions in 7.1.
- [x] **7.3** Screenshot the Import screen (table + gallery + a conflict resolver + history) into `docs/new-app/ui-evidence/import-*.png`. *Evidence:* PNGs.
- [x] **7.4** Full regression: `dotnet build` 0 errors; `dotnet test` green across all projects (incl. Architecture); real GUI boots. *Evidence:* run output.

---

## Traceability (spec § → phase)

| Spec area | Phases |
|---|---|
| §3 classification + rules 1/2 | 1 |
| §4 recommender | 2 |
| §6 archives + temp + cleanup | 3, 4.1, 5.5 |
| §7 apply pipeline (copy/fix/rename/quarantine) | 5 |
| §8 persistence + history | 5.6, 6.5 |
| §5 SDK contracts | 0.2, 4, 5 |
| §10 UI (table/gallery/resolver/sources/history) | 6 |
| §2 full flow proven on real data | 7 |

## Suggested slicing for delivery
1. **Slice A (engine core):** Phases 0–2 + 4.1 — scan+classify+recommend visible via a CLI/test, no copy yet.
2. **Slice B (archives):** Phase 3 + 4.2–4.4 — multi-source + extraction + failed-source handling.
3. **Slice C (apply):** Phase 5 — durable copy, fix, rename, quarantine, history, cleanup.
4. **Slice D (UI):** Phase 6 — the screen, table/gallery, resolver, history overlay.
5. **Slice E (proof):** Phase 7 — real-data E2E + regression + screenshots.

---

## Deferred enhancements — ALL CLOSED (2026-07-20)

Every previously-deferred item is now implemented + evidence-gated (no outstanding debt):
- **6.3** gallery real previews — per-file extraction at scan → path→`Bitmap` converter (`ImportGalleryPreviewTests`
  + real-data `import-02-gallery.png`).
- **6.6** keyboard — `J/K`/`↑↓` nav, `] [ \` decisions, `Del` discard, tunnelled (`ImportViewModelTests`).
- **5.7** `VarsImported` event + `imports.applied`/`imports.vars_copied`/`imports.apply.duration` metrics
  (`ImportApplyEffectsTests`).
- **5.9** activate-after execution — accumulates imports into a durable "Imported" preset + builds profile links
  (`ImportApplyEffectsTests`; real symlink build env-gated via `ImportRealRunE2ETests`).
- **5.11** settings-UI — `import.temp_dir` + `import.history_keep` on the Settings ▸ Import tab (`SettingsScreenTests`).

---

## Completeness audit vs docs 29 (draft) + 30 (spec) — 2026-07-20

Full trace of every draft/spec item to code (not trusting the checkboxes above). Found the checklist had over-reported
several 6.x/§ items as done; the gaps below were then implemented + wired + tested. **App/Infra/E2E all green.**

| # | Gap (spec/draft) | Fix | Evidence |
|---|---|---|---|
| G1 | Resolver showed one fixed conflict-button set for every lane | Per-lane sections: New→Import/Skip, Exact→Skip/Import, CJK→Import+fix/Import/Skip, Naming→rename/keep/discard, Conflict→keep-new/old/both/discard, Corrupt→discard/import-anyway | `ImportViewModelTests`, `import-03-resolver.png` |
| G2 | Naming lane never showed filename-vs-meta identity compare | Identity compare grid (D3) in resolver | `import-03` |
| G3 | `ImportOutcomeEntity` (spec §8) not persisted — only run+failed-sources | Entity + DbSet + migration `AddImportOutcome`; per-item outcome recorded in `ApplyAsync`; `ImportRun.Outcomes` mapped | `ImportApplyEffectsTests.Apply_persists_per_item_outcomes` |
| G4 | Dupes "Download intake" tab not retired (§10) | Tab + VM members + view removed; Import screen replaces it | `TabAdoptionTests`, `GapATabWiringTests` |
| G5 | Import rail badge (review count) never set | `ImportViewModel.ReviewRemaining` → `shell.SetBadge("import", …)` | `import-03.png` shows badge **7** |
| G6 | Startup orphan-sweep of temp workspace not wired | `IImportService.SweepTempWorkspacesAsync` + `AppHost.SweepImportTemp` at launch | `ImportApplyEffectsTests.Sweep_removes_orphaned_temp_dirs` |
| G7 | No list search/filter box | Search box → `SearchText` filter (name/creator) | `ImportViewModelTests.Lane_flags_apply_plan_and_search_filter` |
| G8 | No live apply-plan note | "Copy N · fix M · bỏ qua K · loại L" bound to `ApplyPlanSummary` | same test + `import-03` |
| G9 | Telemetry §9 partial | Added `imports.scanned/copied/fixed/failed` counters + `imports.temp.bytes` histogram | `…Scan_records_scanned_metric` |
| G10 | 7z-binary fallback (`import.sevenzip_path`, §6 A1) never read | Optional external 7-Zip fallback (setting → auto-detect) when SharpCompress can't read an archive | `ArchiveExtractor` |
| G-UI | Draft parity: lane-colour rows, decided/pending pill, tier badge, stepper, sources summary, worknote, progress bar, gallery/table creator, history skipped-count + "Thử lại" retry | All wired | `import-01..03.png` |
| G-rob | Shell live-feeds poll threw `ObjectDisposedException` on shutdown/teardown race | `ShellLiveFeeds.SnapshotAsync` returns empty snapshot on disposed provider | real-run E2E now stable across reruns |
