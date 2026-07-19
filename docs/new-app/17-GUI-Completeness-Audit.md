# 17 — GUI Completeness Audit (source-vs-prototype)

**Method:** read the actual `.axaml`/`.cs` source under `src/VarVault.App/` and compared it item-by-item
against the sealed prototype `docs/new-app/mockups/prototype.html` (736 lines). **The checklist
`16-GUI-Implementation-Checklist.md` claims 61/61 complete — that claim is false at the item level.**
Every screen exists as a thin skeleton (14–46 lines of XAML vs a rich prototype), most interactive controls
are missing, and every dialog + all runtime data wiring is dead.

Legend: ❌ missing entirely · ⚠ present but broken/unwired · ✅ present & wired.

---

## A. Systemic defects (affect the whole app)

1. **❌ All 10 dialogs are unreachable.** `AddRepoDialog`, `MigrateDialog`, `OnboardingDialog`, `FixDialog`,
   `AliasDialog`, `ConfirmDeleteDialog`, `PresetEditDialog`, `DupeReviewDialog`, `RescueDialog`, `VarDetailDialog`
   compile but **nothing opens them** — grep finds no `ShowDialog`, no `.Show(`, and no `ModalHost` instance in
   `MainWindow.axaml` or any screen. The `ModalHost` control exists but is never placed. Every "Review…",
   "Resolve…", "Plan…", "Fix", "Setup wizard", "Edit alias", "Add repository", "Rescue" button that opens a modal
   in the prototype is a no-op.
2. **⚠ Runtime data wiring is 100% dead.** These `ShellViewModel` members are defined but **never called** from
   any production code (`AppHost.CreateShell` wires none of them):
   - `RefreshJobs(...)` → **Jobs panel always shows "No background jobs."** (prototype shows 3 live jobs).
   - `SetBadge(...)` → **rail badges never appear** (prototype: Proposals 7, Health 725, Missing 1.2k).
   - `IndexStatus` / `TierSummary` / `SelectedCount` → **log dock renders blank**.
   - `RescueHandler` / `AddRepoHandler` → **top-bar "⛑ Rescue" and "+ Add repository" invoke null** → do nothing.
   - `ShowToast(...)` → never triggered by any real action.
3. **❌ No indexing trigger in the GUI.** `IIndexOrchestrator` is never referenced from `VarVault.App`.
   A fresh launch never indexes, so every screen shows empty data. (BE-N0 exists but only tests call it.)
4. **❌ No tabbed sub-navigation anywhere.** 7 prototype surfaces use tabs — Tiering (4), Dupes (4), Health (3),
   Proposals (5), Trash (2), Settings (5), Var-detail modal (4). Zero implemented (`TabControl`/`Tabs` grep = 0 hits).
5. **❌ Rail branding gone.** No logo block, no "VarVault / 69,660 pkgs" subtitle, no per-item SVG icons.
6. **⚠ Ops/action buttons render without `Command`** — visually present, functionally dead (details per screen).

---

## B. Per-screen item census

### Top bar — ⚠ visually complete, functionally dead
Search input ✅(bound, but no command-palette action) · ⛑ Rescue ⚠(null handler) · ⟳ Jobs + unread dot ✅ ·
+ Add repository ⚠(null handler) · ◐ theme toggle ✅.

### Jobs panel — ⚠ shell present, never populated
Header ✅ · "pause all" ✅ · **3 live job rows ❌** (indexing / migrating / batch-fix — never fed).

### Log dock — ⚠ blank at runtime
❌ spinner animation · ⚠ index status (unbound source) · ⚠ selected count (unbound) · ⚠ storage tier % (unbound).

### Rail — ⚠ nav works, chrome missing
✅ 4 groups + 13 items + active state. ❌ logo/branding · ❌ SVG icons · ❌ badges (never set).

### Dashboard — 3 of 6 cards, 0 interactions
✅ Storage-by-tier card (bar) — ❌ temp dots, tier labels, "rebalance" link.
✅ Classification card (3 counts) — ❌ stacked bar, "tune →" link.
⚠ Needs-attention card — only 2 text lines; ❌ the 4 clickable rows w/ icons + actions.
❌ **Reclaimable-space card** (590 GB stat + breakdown + wizard button).
❌ **Recent-activity card** (3 rows + "full history →").
❌ **Quick-actions card** (3 buttons).
❌ screen-head "Setup wizard" + "Rescue" buttons. ❌ every click-navigation (0 `Command=` in the whole view).

### Library — the biggest gap
**Left rail:** ✅ 2 buttons (All, Favorites). ❌ 4 more saved views (Active in game, Single copy, Unrecognized,
Recently added) · ❌ per-view colored dots + counts · ❌ **Tags** section + "new tag" (+) button + tag rows ·
❌ **Collections** section · ❌ **Maintenance** tools (Rebuild symlinks, Batch-fix encoding, Find duplicates,
Find stale — with badges) · ❌ **Dependency analysis** sub-actions (Scan missing installed/all/Saves, Analyze VaM log).
**Facet bar:** ⚠ creator combo is a plain `AutoCompleteBox` (❌ no per-creator counts) · ✅ search · ✅ packageName ·
⚠ has "Favorites" checkbox but prototype's is **"Installed"** · ⚠ "Refresh" button not in prototype ·
❌ **Reset** button · ❌ filter chips (All types ×, Tier ×, + Filter) · ❌ "rows 1–48 of N" position · ❌ **Sort dropdown** (4 options).
**Table:** present cols = Name/Creator/Type/Size/Class/Deps(!). ❌ row **checkbox** column · ❌ **6 per-type count
columns** (Sc/Lk/Cl/Hr/Pl/Mo) · ❌ **Tier** col w/ temp dot · ❌ **Copies** col · ❌ **State** pill (ok/missing/needs-fix) ·
❌ **Fix Var** (rebuild) col · ❌ **Detail** button · ❌ sort carets · only 3 of 6 headers sortable.
**Gallery:** ⚠ crude card; ❌ tier/crit/warn badges · ❌ "installed" pill · ❌ type-colored thumb.
**Ops bar:** ✅ selected count · ⚠ Add-to-preset / Delete / Fix-encoding **have no `Command`** · ✅ Export→txt.
❌ "select all N matching" link · ❌ **Install** · ❌ **Uninstall** · ❌ **Move to subfolder…** · ❌ **Install from txt**.
**Detail panel:** ✅ name/creator/type/class/online-copies/single-copy note/missing note. ❌ hero image · ❌ tag row
(hot/T1/installed/favorite) · ❌ 4 action buttons (Uninstall, Favorite, Locate, Open full detail) · ❌ **Content-previews
section** (type dropdown + Loadable-only + Hide/Fav + 3×2 preview strip) · ❌ **Dependencies list** w/ state pills + "resolve via alias" · ❌ **Copies list**.

### Repositories — cards stripped
✅ tier badge, name, online/offline tag, media type, mount path. ❌ screen-head "Review rebalance plan" +
"Add repository" · ❌ spec line (speed / var count) · ❌ **usage bar** · ❌ capacity + reserve line · ❌ per-card
buttons (Re-benchmark, Tier ▾, Edit, Rebalance) · ❌ offline-card dimmed variant.

### Presets — detail gutted
✅ preset list (name+count) · ✅ closure/missing counts · ✅ Switch button. ❌ screen-head "Import from txt…" +
"New preset" · ❌ per-preset dots · ❌ "active in game" tag · ❌ Edit / Diff / Export buttons · ❌ infobox
(deps/aliases/missing + resolve) · ❌ **member table** (Package/Resolution/State) · ❌ "Deactivate all".

### Tiering — half a screen
✅ 3 class count cards · ✅ misplaced list (name/class/tiers/why). ❌ screen-head "Simulate policy…" +
"Review migration plan" · ❌ 4 tabs · ❌ bars on the class cards · ❌ per-row **Plan…** button · ❌ **Size** column.

### Duplicates & reclaim — mostly absent
✅ one "reclaimable bytes" line · ⚠ exact-groups list w/ Review (no `Command`). ❌ 4 tabs · ❌ **3 reclaim summary
cards** (Duplicate copies / Cold-on-SSD / Never-loaded) + their buttons · ❌ group Locations + Reclaim columns.

### Health & fix — one card
✅ encoding groups (codepage+count). ❌ screen-head "Fix all detected…" · ❌ 3 tabs (Encoding/Integrity/Missing-meta) ·
❌ 3 summary cards (GBK/Shift-JIS/Low-confidence) + Fix buttons · ❌ detail table (Detected/Broken/Confidence/Fix).

### Missing deps — columns dropped
✅ ref + needed-by count · ⚠ Resolve… (no `Command`). ❌ screen-head "Export links txt" · ❌ **Alias-to-owned**
column · ❌ **Scope** column · ❌ functional resolve (opens nothing).

### Proposals — chrome dropped
✅ proposal cards w/ title/detail/Reject/Approve (wired). ❌ screen-head "Reject all" + "Approve selected" ·
❌ 5 tabs · ❌ per-card **checkbox** · ❌ per-card **icon** · ❌ **tag** (verified/high-conf) · ❌ **Review…** button.

### Analytics — text, no charts
✅ Space-by-type + Top-by-creator (text rows). ❌ **bars** on both · ❌ **Wasting-fast-storage** card ·
❌ **Usage-over-time** sparkline card.

### Trash & backup — bulk ops missing
✅ Back-up-now + per-row Restore/Purge. ❌ 2 tabs (Trash / Catalog backups) · ❌ row **checkbox** column ·
❌ summary line (count/size) · ❌ **Restore selected** / **Purge selected** bulk buttons · ❌ **Trashed** time column ·
❌ Catalog-backups tab content.

### Activity history — filter missing
✅ When/Action/Description rows. ❌ action-filter dropdown (All/Migrations/Deletes/Fixes) · ❌ tag styling on Action.

### Settings — one tab, wrong controls
✅ VaM install path (textbox) + Save. ❌ 5 tabs · ❌ Catalog-DB-location field · ❌ **Symlink-type dropdown** ·
⚠ Fix-on-import is a **textbox** (prototype: dropdown Flag/Prompt/Auto) · ❌ Preset-extraction-defaults row.

---

## C. Dialog internals (all unreachable per A.1; also simplified)

- **Onboarding** ❌ 4-step stepper · ❌ benchmarked-folder table.
- **Add repository** ❌ Browse… button · ❌ tier dropdown · ❌ "rebalance existing" checkbox.
- **Migrate** ❌ copy→verify→rename→delete **flow stages** · ❌ file table · ⚠ Dry-run/Approve have no `Command`.
- **Fix encoding** ❌ before/after mojibake preview · ❌ "also slim" checkbox · ⚠ apply button no `Command`.
- **Alias** ❌ owned-package search box · ❌ scope dropdown.
- **Confirm delete** ❌ reverse-dependency note · ✅ protected-row pill.
- **Preset edit** ❌ Add-from-filter / Import buttons · ❌ member table w/ version-pin checkboxes.
- **Dupe review** ❌ keep-one **radio** table.
- **Rescue** ❌ baseline-preset dropdown.
- **Var detail** ❌ 4 tabs (Overview/Dependency graph/Content items/Copies & lineage).

---

## D. Headline
The prototype has ~460 discrete UI items; the app implements roughly a fifth of them, and of what *is* drawn a
large share has no `Command`/data source. **The GUI is a static shell, not a working application:** no dialog can
be opened, no job/badge/log-dock ever populates, and nothing triggers indexing. The backend engines audited as
"faithful but unreachable" remain unreachable through the GUI.
