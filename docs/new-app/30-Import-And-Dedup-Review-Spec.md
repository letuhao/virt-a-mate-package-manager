# 30 — Import &amp; Dedup-Review — Design Spec  ·  **SEALED 2026-07-20**

> Status: **sealed** after edge-case review — see [§13 Decisions Log](#13-decisions-log--sealed). Re-open only with a
> new dated entry there. Build against [31-Import-Implementation-Checklist.md](31-Import-Implementation-Checklist.md).

Feature: a single **Import** surface that replaces the user's manual pipeline
(`VaMHelper` → sort into buckets → Beyond Compare → copy into repo → open app to install).
UX draft: [29-Import-Review-UX-Draft.html](29-Import-Review-UX-Draft.html). Implementation checklist:
[31-Import-Implementation-Checklist.md](31-Import-Implementation-Checklist.md).

Design language + architecture rules per [CLAUDE.md](../../CLAUDE.md) and
[11-Architecture-and-Modularity.md](11-Architecture-and-Modularity.md): modules talk through SDK contracts + events;
catalog writes go through `IWriteQueue`; expected failures return `Result`/`Result<T>`; match identity by the fold key.

---

## 0. Goal &amp; non-goals

**Goal.** From one screen: pick one-or-more sources (folders **and** archives), auto-extract archives to a temp
workspace, scan/validate every `.var`, classify each against the target repo, let the user resolve only the
genuinely-ambiguous cases (fast, keyboard-driven, table **or** gallery), then apply — copy approved vars into an
**explicit target repo/tier**, auto-fix CJK encoding, rename mis-named vars to their meta identity, quarantine
corrupt files — then clean up the temp workspace and record the run in a persisted **import history**.

**Non-goals (v1 of this feature).** Hub download; installing/activating is a *separate optional step after* import
(existing activation flow); no fuzzy/ML similarity; no editing var contents beyond the existing encoding-fix.

**Two hard rules from the user (shape the whole design):**
1. **Never compare versions.** In VaM different versions are effectively different packages and dependencies pin
   exact versions, so *all* versions are kept. A `Creator.Package.V` not already in the repo is simply **New** —
   never "an upgrade of" another version, never a replace suggestion.
2. **Filenames are not trusted.** Garbage/renamed vars exist, so a filename whose identity disagrees with the
   var's own `meta.json` is a first-class **warning lane**, not silently matched.

---

## 1. Current state (starting point — verified in code)

| Piece | Status | File |
|---|---|---|
| `IIntakeService.ClassifyFolderAsync` | **read-only**, returns `IntakeItem(FileName, Classification, SuggestedAction)` — 3 display strings, no decision/copy | [IIntakeService.cs](../../src/VarVault.Sdk/Library/IIntakeService.cs), [EfIntakeService.cs](../../src/VarVault.Infrastructure/Library/EfIntakeService.cs) |
| `IntakeClassifier` (5-way: ExactDuplicate / SameNameDifferentContent / NearDuplicate / EncodingVariant / New) | exists, pure/static | [IntakeClassifier.cs](../../src/VarVault.Domain/Dedup/IntakeClassifier.cs) |
| Structural signatures (`ContentSignature`, `PayloadSignature`, `ContentSignatureNoPath`) — CRC+size+name multiset, compression/order independent | exists (superior to VaMHelper's whole-file MD5) | [ContentSignatureEngine.cs](../../src/VarVault.Domain/Fingerprinting/ContentSignatureEngine.cs) |
| Integrity detection `IntegrityStatus` (Ok/CorruptZip/MissingMeta/BadName); meta divergence `MetaCreator/MetaPackage/MetaDivergent` | exists on `VarFile`/`Package` | [Catalog.cs](../../src/VarVault.Domain/Entities/Catalog.cs), [VarInspector.cs](../../src/VarVault.Infrastructure/Indexing/VarInspector.cs) |
| Encoding fix (GBK/Shift-JIS → UTF-8, re-zip) | exists | `IEncodingFixer` [IEncodingFixer.cs](../../src/VarVault.Domain/Content/IEncodingFixer.cs), [EncodingFixCoordinator.cs](../../src/VarVault.Infrastructure/Indexing/EncodingFixCoordinator.cs) |
| Durable file move (copy→verify→rename→delete) | exists, reuse the pattern | `MigrationRunner` [MigrationRunner.cs](../../src/VarVault.Infrastructure/Indexing/MigrationRunner.cs), `IMigrationService` |
| Single-writer catalog writes | `IWriteQueue` + `IUnitOfWork` | [IWriteQueue.cs](../../src/VarVault.Sdk/Threading/IWriteQueue.cs) |
| "Download intake" tab (display-only list) | to be replaced by the Import screen | [DupesViewModel.cs](../../src/VarVault.App/ViewModels/DupesViewModel.cs), [DupesView.axaml](../../src/VarVault.App/Views/DupesView.axaml) |
| 7z/rar extraction | **missing** — only `System.IO.Compression.ZipArchive` is used | — |
| Per-item decision state, copy-into-repo, conflict recommendation, import history | **missing — this feature adds them** | — |

---

## 2. End-to-end flow

```
1 Sources     pick N folders + N archives; choose target repo (+ tier)
2 Extract     each archive → temp workspace (%LOCALAPPDATA%\VarVault\import\<sessionId>\<archiveName>\)
              password / corrupt archive → recorded as a failed source (not blocking), surfaced + logged
3 Scan        enumerate *.var across folders + extracted temp; inspect each (reuse IVarInspector);
              classify into 6 lanes; compute per-item signals; auto-recommend for review lanes
4 Review      user resolves conflict / naming / corrupt lanes (table or gallery + resolver + keyboard);
              New/Exact/CJK are auto-decided; "Accept all recommendations" one-click
5 Apply       for each decided item run the import action (copy / rename+copy / import+fix / skip / discard);
              durable copy into target repo tier; index the new vars; (optional) activate afterwards
6 Finish      delete the temp workspace; persist an ImportRun to history (per-item outcome + failed sources)
```

Session is cancellable at any point (`IJobQueue`, `CancellationToken`); temp workspace is always cleaned on
completion **or** cancellation (try/finally), history is written **before** cleanup so failed-source info survives.

---

## 3. Classification model — 6 lanes (maps onto existing signals)

| Lane | Trigger (from existing signals) | Default | Needs review? |
|---|---|---|---|
| **Exact dup** | same `IdentityKey` + same `ContentSignature` in repo | skip | no (auto) |
| **CJK encoding** | valid var, but `DetectedCodepage` non-UTF8 / `BrokenEntryCount>0` (GBK/Shift-JIS names) | **import + fix to Unicode** (via `IEncodingFixer`) | no (auto, non-blocking) |
| **Conflict** | same **full** `IdentityKey` (incl. version) present, but `ContentSignature` differs | — | **yes** |
| **Name ≠ meta** | `MetaDivergent` — filename identity ≠ meta.json's `MetaCreator.MetaPackage` | **no default — user picks per var** | **yes** |
| **Corrupt** | `IntegrityStatus` ∈ {CorruptZip, MissingMeta, BadName} | discard | **yes** (confirm) |
| **New** | none of the above (incl. a version you don't have) | import | no (auto) |

Precedence when several could apply: **Corrupt &gt; Name≠meta &gt; Conflict &gt; CJK &gt; Exact &gt; New**.
Rule-1 note: the old `NearDuplicate`/`EncodingVariant` cross-identity classes stay for the *Duplicates* screen but
are **not** used to compare versions here. A different-version file is `New`.

**Dedup scope = the whole library, not just the target repo (D1).** Classification checks the candidate against
**every** catalogued var across **all** repositories (hot/warm/cold). A var already present in any repo → **Exact dup
→ skip** (with a "đã có ở &lt;repo&gt;" hint; if it's in a colder repo than the target, offer *promote/move* instead of
a duplicate copy — a nudge, not an auto-action). This requires the catalog to hold complete signatures for all repos;
if the target/other repos aren't fully indexed, the scan **indexes them first** (or warns which repo is stale) so the
dedup check is trustworthy.

**Name≠meta cross-check (E1).** For a garbage-named var whose `meta.json` is well-formed, also match the **meta
identity + content signature** against the catalog: if that resolves to an existing var, the item is really a
misnamed **Exact/Conflict**, not a plain New — reclassify accordingly instead of importing a garbage-named copy.
If both filename **and** meta are malformed → keep-as-is + review (never auto-rename to a bad meta).

---

## 4. Conflict recommendation engine (Domain, pure)

Only conflict-lane items need a "which is correct" suggestion. `ConflictRecommender.Recommend(incoming, existing)`
returns `(Decision, Reason)` using a deterministic priority ladder (first decisive signal wins):

1. **Validity** — one side `CorruptZip`/`MissingMeta`, the other valid → keep the valid one. *(most common real case)*
2. **Meta identity** — one side `MetaDivergent`, the other meta-matches → keep the matching one.
3. **Encoding health** — identical payload but one side already UTF-8-clean and the other has GBK entries → keep the clean one.
4. **Entry count / size** — materially more entries or larger uncompressed size (truncated-download guard) → keep the fuller one.
5. **File mtime** — **weakest** tiebreak only, and labelled "(download date, not authoritative)" in the UI — a file
   downloaded today can be an *old* var, so mtime never overrides validity/meta/size.
6. **Ambiguous** (both valid, both meta-ok, similar size, real content diff) → recommend **Keep both** (rename incoming
   with a non-colliding `(n)` suffix) and surface the per-entry diff so the user decides.
7. **Both broken** (incoming and existing both Corrupt) → recommend **Keep existing / skip** (never overwrite one
   broken copy with another) and flag for manual attention.

Each step yields a human `Reason` string shown in the resolver ("Bản trong repo là zip hỏng — 12 entry lỗi CRC…").
The per-entry diff (added/removed/changed) is derived from the two vars' `ZipEntryFacts` (name+CRC+size) — available
at inspection, no full decompress. Recommendations are **advisory**; the user always overrides.

---

## 5. SDK contracts (new)

```csharp
// VarVault.Sdk/Import/*
public enum ImportLane { New, Exact, Cjk, Conflict, Naming, Corrupt }
public enum ImportDecision { None, Import, Skip, KeepIncoming, KeepExisting, KeepBoth, RenameToMeta, ImportAndFix, Discard }

public sealed record ImportSignals(                    // everything the resolver/gallery shows, from IVarInspector
    bool ValidZip, string IntegrityStatus, long SizeBytes, int EntryCount, DateTime FileMtime,
    bool HasPreview, string? PreviewThumbPath, string FilenameIdentity, string? MetaIdentity,
    bool MetaDivergent, string? Codepage, int GbkEntryCount);

public sealed record ImportItem(
    Guid Id, string SourceLabel, string SourcePath, string FileName, string IdentityKey,
    ImportLane Lane, ImportSignals Signals, ImportDecision Recommendation, string Reason,
    ExistingRef? Existing, IReadOnlyList<EntryDelta> Diff, ImportDecision Decision);
public sealed record ExistingRef(long VarFileId, int Tier, string Path, ImportSignals Signals);
public sealed record EntryDelta(char Kind /*+ - ~*/, string Path);

public sealed record ImportSource(string Path, ImportSourceKind Kind /*Folder|Archive*/, ImportSourceStatus Status,
    int VarCount, string? FailReason /*password|corruptArchive*/);

public interface IImportService {
    Task<ImportSession> ScanAsync(ImportSpec spec, IProgressSink progress, CancellationToken ct);   // extract+classify
    Task<ApplyResult>   ApplyAsync(ImportSession session, CancellationToken ct);                    // copy+fix+rename+cleanup
    Task<IReadOnlyList<ImportRun>> HistoryAsync(int take, CancellationToken ct);
}
public sealed record ImportSpec(IReadOnlyList<string> Paths, Guid TargetRepositoryId, bool ActivateAfter = false);
// Target = a repository (its tier is the repo's own tier — no separate tier arg). ActivateAfter = optional post-import step.
public sealed record ImportSession(Guid Id, IReadOnlyList<ImportSource> Sources, IReadOnlyList<ImportItem> Items);
public sealed record ApplyResult(int Copied, int Fixed, int Renamed, int Skipped, int Discarded, int Failed, Guid RunId);

// supporting contracts
public interface IArchiveExtractor {   // zip/7z/rar → temp dir; detects password / corrupt
    Task<ArchiveExtractResult> ExtractAsync(string archivePath, string destDir, CancellationToken ct); }
public interface ITempWorkspace : IAsyncDisposable {   // scoped temp dir, auto-clean
    string Root { get; } string NewDir(string name); }
public interface IImportHistoryStore {                 // persisted runs
    Task<Guid> RecordAsync(ImportRun run, CancellationToken ct);
    Task<IReadOnlyList<ImportRun>> RecentAsync(int take, CancellationToken ct); }
```

`ImportSession` holds mutable `Decision` per item (set by the UI before `ApplyAsync`). Decisions default from
`Recommendation` for review lanes only after the user accepts them (or per-item); auto lanes are pre-decided.

---

## 6. Archive extraction &amp; temp workspace

- **Extractor = SharpCompress + a CJK-aware CustomDecoder (amended 2026-07-20, A1).** `ArchiveExtractor` uses
  SharpCompress (pure-managed, `.zip/.7z/.rar/.tar(.gz)`) configured with
  `ArchiveEncoding.CustomDecoder = (bytes, i, n) => …` wired to **VarVault's own byte-level CJK detection** (strict
  UTF-8 → GBK → GB18030 → Shift-JIS, the same ladder `EncodingHealthEngine` uses). This is load-bearing: this library
  is heavily CJK and legacy **zip** entry names (the `.var` file names, sometimes in Chinese subfolders) are stored
  without a UTF-8 flag; the CustomDecoder gets the *raw* bytes so those names extract correctly instead of being
  mangled. (7z/rar store names as Unicode natively, so this mainly protects zip.) **Optional 7z-binary fallback**: a
  setting `import.sevenzip_path` (default = auto-detect a system 7-Zip, e.g. `C:\Program Files\7-Zip\7z.exe`; not
  hardcoded) is used to retry any archive SharpCompress can't read (e.g. AES-encrypted 7z). If neither succeeds the
  source becomes a **failed source** (password/corrupt) — logged, handled manually. Nested var-in-archive only.
- **Password-protected / unreadable archive:** `ExtractAsync` returns `ArchiveExtractResult(Failed, reason)` — the
  source is marked `🔒 password` / `💥 corruptArchive`, **excluded from scan**, surfaced in the Sources strip, and
  written to history so the user can handle it manually. Never blocks the rest of the run. *(SharpCompress supports
  zip/rar passwords; some 7z AES-header variants may fail → they simply fall into this failed bucket.)*
- **Temp workspace (T1 = configurable):** root from a setting `import.temp_dir`, **default = a `.varvault-import\<sessionId>\`
  dir on the target repository's drive** (fast same-drive copy, doesn't fill C:); fallback `%LOCALAPPDATA%\VarVault\import\`.
  Each archive extracts to its own subdir. Before extracting, **pre-check free space** on the temp drive vs the archive's
  uncompressed size; abort that source with a clear "not enough space" reason (logged) if short. Extracted `.var` files
  are validated **exactly like folder vars** (same `IVarInspector` path). Workspace is deleted in `ApplyAsync`'s `finally`
  (and on cancel/dispose); a stale-workspace sweep on app start removes orphans from crashes.

---

## 7. Apply pipeline (per item, durable, via `IWriteQueue`)

| Decision | Action |
|---|---|
| `Import` / `KeepIncoming` | durable copy into `targetRepo/<tier>` (copy→verify hash→rename), then index (catalog write via `IWriteQueue`) |
| `RenameToMeta` | copy as `MetaCreator.MetaPackage.Version.var` (fold-key safe), then index |
| `ImportAndFix` | copy, then run `IEncodingFixer` on the copy (GBK→UTF-8 re-zip), verify, index |
| `KeepBoth` | copy incoming with a non-colliding `…(2).var` name; keep existing untouched |
| `KeepExisting` / `Skip` | no file op; record as skipped |
| `Discard` | move to quarantine (`___VarInvalid___`), never into the live repo |

**Encoding-fix is a modifier, not a lane action (E2).** Any imported var with GBK/Shift-JIS entries is run through
`IEncodingFixer` on the copy — including a Conflict item resolved as *Keep incoming* — not only pure CJK-lane items.
If the fix fails (un-mappable names), import the **original** and record "encoding fix failed" in the run.

Durability reuses the `MigrationRunner` copy→verify→rename→delete approach (`synchronous=FULL` per destructive step).
Catalog mutations (new `VarFile`/`Package` rows, quarantine marks) happen inside `IWriteQueue` actions with
`IUnitOfWork`. On any per-item failure the item is marked `Failed` in the run (with reason) and the pipeline continues.
After apply, emit `VarsImported` domain event so Library/Dashboard refresh.

**Optional post-import activate (D2).** When `ImportSpec.ActivateAfter` is set, after a successful apply the pipeline
builds/activates a preset from the just-imported vars into the configured VaM install (reusing the existing activation
flow) — a single "import + install" click. Off by default; activation otherwise remains a separate screen.

---

## 8. Persistence (EF Core + SQLite, new migration)

- `ImportRunEntity(Id, StartedUtc, TargetRepositoryId, SourceSummary, Copied, Fixed, Renamed, Skipped, Discarded, Failed)`
- `ImportOutcomeEntity(Id, RunId, FileName, IdentityKey, Lane, Decision, Result /*ok|failed*/, Reason)`
- `ImportFailedSourceEntity(Id, RunId, Path, Kind, Reason /*password|corruptArchive*/)`

History screen/overlay reads these. Scan results themselves are **not** persisted (a session is transient until
applied) — only the applied run + failures are durable. Add one EF migration; keep WAL + FK pragmas per baseline.

---

## 9. Events, telemetry, threading

- Domain event `VarsImported(RunId, Count)` (past-tense `IDomainEvent`) on the `IEventBus`.
- Telemetry (`VarVault` meter): counters `import.scanned/copied/fixed/failed`, histogram `import.apply.duration`,
  gauge `import.temp.bytes`.
- All long work (`ScanAsync`, `ApplyAsync`, extraction) runs on `IJobQueue` with progress + cancellation; UI thread
  work via `IUiDispatcher`; `ConfigureAwait(false)` in library code.

---

## 10. UI (Avalonia) — a first-class **Import** rail screen

Replaces the Dupes "Download intake" tab. New `ImportViewModel` + `ImportView` (screen id `import`, rail group
Optimize, with a live badge = review-lane count). Structure mirrors the draft:

- **Top bar:** target-repo selector (`Import into: <repo> · <tier>`), `History` button, theme, Re-scan.
- **Sources strip:** folder/archive chips with status (extracted / 🔒 password / 💥 corrupt) + "Add folder/archive…".
- **Triage chips:** the 6 lanes with counts; review lanes emphasized; "N mục cần bạn quyết" note.
- **Work area (split):** list pane with **Table ⇄ Gallery** toggle (gallery reuses the Library thumbnail loader
  `IThumbnailStore`/`ThumbnailLoader` so meaningless names are recognizable by preview) + a **resolver** pane:
  - conflict → side-by-side incoming/existing signal grid + per-entry diff + recommendation banner + Keep new/old/both/discard;
  - naming → filename-identity vs meta-identity + rename/keep/discard (**nothing pre-selected — user picks per var, D3**);
  - corrupt → integrity reason + discard/import-anyway;
  - CJK → "will fix to Unicode" toggle; New/Exact → auto-decision detail.
- **Action bar:** review progress, "Accept all recommendations", per-outcome summary, an **"Activate into VaM after
  import"** checkbox (default off, D2), and **Apply →** (disabled until every review-lane item is decided).
- **History overlay:** past runs (✓ copied / ↷ skipped / ✗ failed) with failed-source rows (password/corrupt) + "Retry…".
- Keyboard: `J/K` navigate review items, `] [ \` decisions, `Del` discard.

Wire in `AppHost.CreateShell` + `ShellViewModel.AllScreens`; badge fed by `IShellLiveFeeds`.

---

## 11. Edge cases &amp; decisions

- **Same var in two incoming sources** (folder + archive) → deduped within the session by `ContentSignature`; the
  first wins, the rest shown as intra-batch duplicates (auto-skip).
- **Target repo offline / full** → block Apply with a clear message (reuse repo capacity/online checks).
- **Rename-to-meta collides** with an existing repo var of that identity → becomes a **Conflict**, not a silent overwrite.
- **Discard** never deletes the user's source file; it quarantines the *imported copy attempt* only. Source folder/archive
  is left as-is (the user chose it read-only).
- **Crash mid-apply** → durable copy means no half-written repo file; temp workspace swept on next launch; a **crashed**
  run leaves no history row. A **graceful cancel** mid-apply *does* record a partial run (items already applied + the
  remainder marked skipped), so history reflects reality.
- **Only one import session at a time** — the UI blocks starting a second scan/apply while one is active (avoids temp +
  target-repo races; catalog writes are already serialized by `IWriteQueue`).
- **History retention** — keep the most recent **200** runs by default (`import.history_keep` setting) + a manual prune,
  oldest-first.

---

## 12. Out of scope (v1)

Hub fetching of missing deps; recursive archives (archive-in-archive); fuzzy/ML similarity; in-app password entry for
protected archives (logged as failed, handled manually); one-click "undo import run" (imported vars are removed via the
normal Library/Duplicates delete); editing var contents beyond encoding-fix.

---

## 13. Decisions Log — SEALED

Reviewed &amp; sealed 2026-07-20. **B-items** answered by the user; **A-items** resolved with defaults during review.
Change any of these only with a new dated entry here + a bump to the affected checklist item.

**Amendments (post-seal):**
- **A1 · 2026-07-20 — Archive extractor CJK strategy (web-checked).** Concern: managed zip libs mangle legacy-CJK
  entry names. Research confirmed SharpCompress (0.50.x) exposes `ArchiveEncoding.CustomDecoder(byte[],int,int)→string`
  giving the **raw** name bytes, so it *is* solvable pure-managed. Decision: SharpCompress + a CustomDecoder wired to
  VarVault's own CJK detection (primary); an **optional** `import.sevenzip_path` setting (auto-detected system 7-Zip,
  not hardcoded) as a fallback for archives SharpCompress can't read; otherwise → failed source. No native binary is
  bundled by default. `IArchiveExtractor` contract unchanged. (§6)

**User decisions (business):**
- **D1 · Dedup scope = whole library.** Check the candidate against every catalogued var across all repos; already-present
  anywhere ⇒ skip (with a promote/move nudge if it's in a colder repo). Requires complete catalog signatures — the scan
  indexes stale/unindexed repos first, or warns which repo is stale, before trusting the dedup result. (§3)
- **D2 · Optional activate after import** — a default-off checkbox that, on success, activates the just-imported vars into
  VaM (existing activation flow). Import and activation otherwise stay separate. (§7, `ImportSpec.ActivateAfter`)
- **D3 · Name≠meta has no auto-default** — the user chooses rename-to-meta vs keep-filename (vs discard) per var; the
  resolver offers all three, nothing pre-selected. (§3)
- **D4 · Temp location is a Setting** (`import.temp_dir`), default = the target repo's drive; free-space pre-check before
  extract. (§6)

**Review resolutions (defaults):**
- **E1 · Name≠meta cross-checks meta identity** against the catalog; a well-formed meta that resolves to an existing var
  reclassifies the item to Exact/Conflict (don't import a garbage-named duplicate). Both filename+meta malformed ⇒
  keep-as-is + review. (§3)
- **E2 · Encoding-fix is a copy modifier**, applied to any imported var with GBK entries (incl. Conflict→Keep-incoming),
  not just the CJK lane; fix failure ⇒ import original + log "fix failed". (§7)
- **E3 · Recommender**: validity ▸ meta ▸ encoding ▸ entries/size ▸ mtime(weakest, "download date") ▸ ambiguous⇒KeepBoth;
  both-corrupt ⇒ keep-existing/skip. Advisory only. (§4)
- **E4 · Target = a repository** (tier is the repo's), copied to repo root; no separate tier arg. (§5, §7)
- **E5 · Lifecycle**: one active session; temp always cleaned in `finally` + startup sweep; graceful cancel records a
  partial run, crash records none; history capped at 200 (setting). (§11)
- **E6 · Discard/skip never touch the user's source** files; Discard quarantines only the import *copy attempt*. (§11)
- **E7 · Password/corrupt archives** never block; logged to history as failed sources for manual handling. (§6)
