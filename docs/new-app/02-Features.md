# New App — Feature Requirements (brainstorm, high-level)

> Requirement-level only — **what** the app does, not how. No code, no data model.
> Organized by the three core pillars ([00-Core-Feature.md](./00-Core-Feature.md)) + the activation axis + cross-cutting concerns.
> Priority tags: **[core]** must-have for v1 · **[should]** important, soon · **[could]** nice-to-have/later · **[scope?]** needs a keep/drop decision.

---

## Pillar 1 — Repository & Tier Management

### Repositories
- **[core] Register a repository** — point the app at a folder on any drive; it becomes a managed var store.
- **[core] Multiple repositories** across multiple drives, all tracked together as one logical library.
- **[core] Enable/disable a repository** without removing it (e.g. an external drive that's currently unplugged).
- **[core] Remove a repository** from management (without deleting the files).
- **[should] Detect the underlying drive** for each repo — physical disk, media type (NVMe / SSD / HDD), so tiering can reason about it.
- **[should] Online/offline state** — gracefully handle removable/network drives disappearing and reconnecting; mark affected vars unavailable, not lost.
- **[could] Read-only repo detection** (archive drives you don't want written to).
- **[could] Relocate a whole repository** to another drive, updating all tracking.

### Performance profiling
- **[core] Benchmark a repo's real read/write speed** on registration (and on demand) — this is what ranks it into a tier.
- **[should] Manual override** of the measured tier ("treat this as Tier 1 regardless").
- **[could] Re-benchmark on a schedule** or when a drive's behavior changes.

### Tiers & capacity
- **[core] Rank repos into tiers** (fastest = Tier 1 hot path … HDD = cold). Multiple repos may share a tier.
- **[core] Priority ordering** within/across tiers (placement & search order).
- **[core] Live capacity tracking** — free/total/used per repo and per physical drive.
- **[core] Min-free / reserve space** per repo — never fill a drive to 100%.
- **[should] Capacity alerts** — warn *before* a repo/tier/drive fills, not after.
- **[should] Per-repo summary** — var count, total size, hot/warm/cold breakdown, health.
- **[should] Add-a-drive flow** — when a new repo/drive is registered, offer to **rebalance** existing data onto it to relieve fuller drives.
- **[could] Drive health awareness** — surface SMART/failing-drive signals so at-risk data can be moved off.

---

## Pillar 2 — Classification & Placement (the analyzer)

### Usage tracking (signals)
- **[core] Record every activation/load the app performs** — the primary, most-trusted signal (timestamp + var).
- **[core] Frequency & recency scoring** over configurable windows (e.g. last used, count in last 30/90 days).
- **[should] Dependency-centrality signal** — a var many things depend on is structurally hot (fed by Pillar 3).
- **[could] Bootstrap from external evidence** — VaM's `output_log.txt` and the dependency graph, to seed hot/cold before the app has its own history.
- **[should] Manual overrides** — pin as hot, force-archive as cold, exclude from auto-migration.

### Classification & policy
- **[core] Classify each var hot / warm / cold** (configurable number of classes) from the combined signals.
- **[core] Placement policy** — map class → tier, respecting capacity + priority.
- **[should] Lifecycle rules** (S3/Azure-style) — time-based ("untouched 60 days → warm; 6 months → cold") and access-pattern based; user-editable.
- **[should] Transparent promote-on-access** — using a cold var auto-promotes it toward hot next cycle.
- **[should] Anti-thrash / hysteresis** — don't bounce a var between tiers on marginal score changes.

### Migration (moving objects between tiers)
- **[core] Migration planning** — compute *what moves where*, how much data, estimated time — **preview before executing**.
- **[core] Automation modes** — manual-only / propose-and-approve / fully-auto (scheduled or threshold-triggered). *(User leans toward reviewing big moves — TBD.)*
- **[core] Safe move** — copy → verify → delete; resumable, cancelable, throttled; never lose a file on interruption.
- **[should] Rebalancing on full tier** — spill to next tier **or** evict the coldest resident (per policy — TBD which).
- **[should] Background execution** with progress + cancel; doesn't block the UI.

### Insight / reporting
- **[should] "Wasting fast storage"** report — cold vars sitting on Tier 1.
- **[should] "Slow where it hurts"** report — hot vars stuck on cold tiers.
- **[core] "Where did my space go?" + reclaim-space view** — space broken down by creator / type / tier; biggest offenders; and a single **reclaim wizard** aggregating duplicates + orphans (nothing depends on them and never loaded) + cold-junk-on-SSD + huge-never-loaded assets, with size estimates and one-confirm cleanup.
- **[could] Analytics dashboard** — usage trends, hot/cold distribution, per-tier utilization over time.
- **[could] Policy simulation / dry-run** — preview the effect of a policy change before applying.

---

## Pillar 3 — Var Metadata & Dependency Management (the catalog)

### Indexing & identity
- **[core] Scan & index vars** — parse `meta.json`, name, content, dependencies, previews; fast + parallel + incremental.
- **[core] Identity parsing** — `Creator.Package.Version`; group all versions of a package together.
- **[core] Incremental re-index** — only re-read changed/new/removed files (via mtime/hash + filesystem watching), not a full rescan.
- **[should] Broken/corrupt detection** — bad ZIP, missing `meta.json`, malformed name → quarantine/report.
- **[core] Encoding-health check during indexing** — flag vars with non-UTF-8 ZIP entry names (see *Var Health & Auto-Fix* below).

### Content & metadata
- **[core] Content-type classification** — scene / look / clothing / hair / morphs / pose / skin / plugins / assets, with counts.
- **[core] Preview images** — extract, cache, thumbnail, display.
- **[should] Rich metadata display** — license, description, creator, dates, size, content summary, program version.

### Dependencies
- **[core] Dependency graph** — parse and store each var's dependencies.
- **[core] Forward closure** — "to use X, you need these" (drives activation + install).
- **[core] Reverse closure** — "these depend on X" (drives safe uninstall/delete).
- **[core] Version resolution** — exact / `latest` / closest-newer-else-newest-older matching.
- **[core] Missing-dependency detection** — what's referenced but not in the library, per var and library-wide.
- **[should] Dependency graph visualization** — see a var's needs/dependents.
- **[should] Distinguish "chosen" vs "pulled-in-only" vars** — mark vars present only because something depends on them (declutter, like vam-backstage).

### Duplicates & versions
- **[core] Logical duplicate detection** — group vars by a **content signature** (the set of inner entries: path + size + CRC), so copies that are the **same inside but packaged into different-sized zips** (different compression/order/timestamps) are correctly detected as duplicates. *This is the specific failure of whole-file-checksum tools (e.g. VaMHelper) that forced manual checking — solved automatically.* Detection is cheap (reads the zip index, no decompression); deletion is verified with a strict hash first.
- **[should] Near-duplicate detection** — same content but different `meta.json` (e.g. re-declared dependencies, or meta rewritten by the encoding-fixer) → flagged separately from exact dups; also catches same content re-uploaded under a different name/version.
- **[should] Download intake classification** — when new vars arrive, auto-sort each vs. the library: *exact dup / logical dup / same-name-different-content / near-dup / new* — instead of dumping ambiguous cases for manual review (improves on VaMHelper's `_notSameContent` bucket).
- **[core] Single-copy / irreplaceable flag** — mark vars that exist in only one place (no duplicate, no backup). The user must *know* what's irreplaceable before any move/delete, since much content can't be re-downloaded.
- **[should] Version management** — list all versions of a package; identify/flag/remove superseded old versions safely (respecting dependents).

### Search, sort, discovery (the "I lost my favorite var" fix)
- **[core] Full-text search** — creator, package, tags, description, filename, content paths — instant, ranked.
- **[core] Fast sort & filter** — by name, size, date added, last used, version, type, tier, dependency count, active/installed, has-missing-deps.
- **[core] Faceted filtering** — combine type + creator + tier + tags + status.
- **[core] Favorites / pins** — first-class, indexed (not loose files like the old tool).
- **[should] Tags / labels** — user-defined, multi-tag, searchable.
- **[should] Collections / groups** — named user sets of vars (e.g. "my project X", "faves for scene Y").
- **[should] Recently used / recently added** views.
- **[could] Notes / rating** per var.
- **[core] Visual gallery — the primary browse mode** — a fast, virtualized, thumbnail wall of the whole library using the extracted preview images, filterable by the facets above. Users think visually ("the redhead look", "that dungeon scene"), not by filename — and won't tag 70K vars by hand. This, more than text search, is the real answer to "I lost my favorite var." Browse regardless of where a var physically lives or whether it's active.
- **[should] Auto-derived facets** — creator, content type, gender, hot/cold, etc. computed automatically so the library self-organizes without manual tagging.

---

## Core Feature — Var Health & Auto-Fix (encoding repair)

> **Why core:** vars packaged on Chinese/Japanese/Korean systems store their ZIP **entry filenames** in a legacy codepage (GB2312/GBK, Shift_JIS, Big5, EUC-KR) **without** the ZIP UTF-8 flag. VaM assumes UTF-8, so the names become mojibake, content paths don't resolve, and **the game fails to load the var.** This is an essential, common problem. Reference logic: the *Boss963 Chinese Vars Fixing Tool* (drag-drop, decode GB2312 → re-zip UTF-8) and the old varManager's multi-codepage `ZipHandler` detector (`docs/varManager/05`). We do it **better: auto-detect across all codepages, and auto-fix in batch** — no manual one-by-one.

- **[core] Auto-detect broken-encoding vars** — during indexing, flag any var whose ZIP entries lack the UTF-8 flag and contain non-ASCII bytes that don't round-trip as valid UTF-8. Identify the *actual* source codepage by trying candidates (GBK/GB2312, Shift_JIS, Big5, EUC-KR, …) and scoring by lossless round-trip + no replacement/control chars. (Superset of Boss963's hardcoded GB2312.)
- **[core] Auto-fix** — decode entry names with the detected codepage and **re-zip with UTF-8 entry names** (ZIP + Deflate, VaM-compatible). Works in batch across the whole library, not one file at a time.
- **[core] Safe output** — write a fixed copy (or overwrite the original *with* a backup/trash), verify the rebuilt var before replacing. Re-index the fixed var (its content hash changes).
- **[should] Health report** — list all vars needing a fix, grouped by detected source encoding; one-click "fix all".
- **[should] Fix-on-import** — when indexing detects the problem, offer to auto-fix immediately (configurable: auto / prompt / just flag).
- **[could] Optional slimming during fix** (from Boss963, all off by default) — delete plugins (`*.cs/*.cslist/*.dll` + `Custom/Scripts`), clothing, hair, assets (`*.assetbundle`), pose, morph cleanup (`RG *` / `pCTRL*`), custom keyword deletion, and set `preloadMorphs=false` in `meta.json`. These are var-shrinking conveniences, kept **separate** from the pure encoding fix.

---

## Activation (cross-cutting axis) — "what does the game see?"

- **[core] Rescue baseline ("just make the game launch")** — the #1 crisis is "I dumped everything in and VaM won't start." One action produces a **minimal, working active set** so the game boots (empty or a small chosen baseline), from which the user adds content deliberately. The tool's first job is to rescue, then to optimize.
- **[core] Activate / deactivate a var** — make it visible/invisible to VaM.
- **[core] Activation mechanism** — symlink into `AddonPackages` (file stays on its tier) — *staging-onto-fast-drive is a TBD option.*
- **[core] Dependency-aware activation** — activating a scene/var pulls its full dependency closure automatically.
- **[should] Temp activation for a one-off var-set** — auto-cleaned after use.
- **[should] Deactivate-all / reset.**
- **[should] Reconcile external changes** — detect vars the user dropped into `AddonPackages` manually and fold them in.
- **[could] Couple activation with hot-promotion** — activating implies "promote toward Tier 1" (policy toggle — TBD).

### Loading presets (named var-sets) — most-used feature; do it better than the old tool
> A **loading preset** = a saved, named set of vars activated together, that the user switches between. This is about *which vars the game sees* (activation) — **not** the out-of-scope in-game scene loader. In the old varManager the nearest equivalent (AddonPackages "switch") was slow and, worse, **lost your missing-var setup on every switch**.

- **[core] Named loading presets** — create / save / edit / duplicate multiple presets, each an explicit var set (and/or rule-based from a filter/collection). Switch between them instantly. See a preset's contents; **diff two presets**.
- **[core] Auto-activate dependencies on load** — loading a preset automatically activates the forward-dependency closure of its vars from the repos. *Better than old:* preview what will be pulled in, touch only what's needed, respect tiers. (The old tool did this — feature #2 the user relies on.)
- **[core] Persistent missing-var aliasing** — when a dependency is missing but a differently-named var you own satisfies it, map (alias) it **once and save it**, so it re-applies automatically on every future load and every preset switch. *This is the old tool's single biggest gap (user feature #3): it could alias missing vars via a GUI but never persisted the setup, so each preset switch meant redoing it.* Aliases stored persistently (global, with optional per-preset overrides), editable, and shareable. Builds on Pillar 3's missing-dependency detection.
- **[core] Import / export presets** — import a preset from a plain txt list of var names (validate on import; flag unknowns and version mismatches); export a preset to txt. Round-trips cleanly; optionally bundles the alias mappings so a preset is fully portable between machines. (User feature #4.)

---

## Cross-cutting / operational

- **[core] First-run onboarding** — wizard: add drives/repos → benchmark → initial scan → first classification → offer the rescue baseline. Must ingest an existing 4 TB mess **as-is**, without the user pre-organizing anything.
- **[core] Background jobs** — indexing, benchmarking, migration all async with progress + cancel; UI never freezes.
- **[core] Settings** — VaM install path, repo/tier config, policies, thresholds.
- **[should] Operation history / audit log** — what moved/activated/deleted and when.
- **[should] Bulk operations** — multi-select activate/deactivate/tag/move/delete.

### Data safety (a design principle, not a feature — the user's library is largely irreplaceable)
- **[core] Never hard-delete** — everything goes to a recoverable trash first; explicit purge only.
- **[core] Verified moves** — copy → checksum-verify → then delete the source. A corrupted move must never lose the only copy.
- **[core] Crash-safe & resumable** — a power loss mid-migration of hundreds of GB loses/duplicates nothing; a reconcile pass repairs state on restart.
- **[core] Dry-run everything** — show exactly what will move/delete and how much data, before doing it. (See Pillar 2 migration planning.)

### Portability & ergonomics
- **[core] Portable catalog** — the library DB (tags, favorites, usage history, active-set profiles) survives a Windows reinstall / PC move; if drives are shuffled, the app **re-finds vars by content**, not by fixed paths.
- **[should] Backup/restore the catalog** — export/import the whole managed state.
- **[should] Remembered views** — persist filters, sorts, column layout, and last view between sessions (the old tool forgot everything — a constant annoyance).
- **[should] Power-user ergonomics** — keyboard-driven navigation, fast multi-select, quick bulk actions (this is a tool used often, at scale).
- **[should] Import from the old varManager** — reuse existing library layout / favorites, and detect the duplicates that years of manual copying created.

---

## OUT OF SCOPE (SEALED — see [10-Decisions-Log](./10-Decisions-Log.md))

These were big features of the old varManager but are **out of scope for v1** (reversible later — Hub/load-into-VaM as optional plugins). They're either separate concerns or content-creation rather than storage/management:

- **[out] Hub integration** — browsing/downloading from hub.virtamate.com, dependency downloading, update checks.
- **[out] Load scenes/presets into a running VaM** (the `loadscene.json` + in-game plugin flow).
- **[out] Scene analysis / preset extraction** (pull morphs/hair/clothing out of a scene).
- **[out] MMD motion loader** — a separate app, not this one.
- **[out] Package your own saves into a var** (PrepareSaves).

---

*Sources for inspiration:* [vam-backstage](https://github.com/cyberpunk2073/vam-backstage) · [iHV](https://github.com/BoominBobbyBo/iHV) · [vam-party](https://github.com/vam-community/vam-party) · [S3 Intelligent-Tiering](https://sedai.io/blog/amazon-s3-intelligent-tiering-storage-optimization) · [Azure Blob lifecycle management](https://learn.microsoft.com/en-us/azure/storage/blobs/lifecycle-management-overview)

*Next: prune/confirm this list, settle the [scope?] items, then prioritize into a v1 cut.*
