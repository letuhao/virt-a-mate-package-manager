# 08 — UI Design Review (draft critique)

Critical evaluation of the [prototype](./mockups/prototype.html) against UX/information-design heuristics, the app's core mission, and the [data architecture](./03-Data-Architecture.md). Findings ranked; each has a concrete fix. **Severity:** 🔴 high · 🟠 med · 🟢 low.

## Verdict
The draft is **strong on coverage** (every feature has a home — the dead-end audit worked) and **right in tone** (dense pro-tool, state-encoded-as-form, real CJK content, theme-aware). But it has **one architectural inconsistency**, several **power-user workflow gaps**, and some **information-architecture redundancy** that would confuse users and cost rework if built as-is. None are hard to fix now.

---

## 🔴 High — fix before build

### DG-1. Paging control contradicts the virtualized-scroll architecture
The Library shows a page pager (`1 / 697`). But we **decided and perf-proved** a materialized read model + **OrderedSnapshot for O(1) virtual scroll** ([05](./05-Perf-Spike-Results.md)) — there are no pages. Shipping page numbers is a metaphor mismatch that misrepresents how the app works and would confuse "jump to row" behavior.
**Fix:** replace the pager with a virtual scrollbar (the whole point of TreeDataGrid). Keep a "row 12,400 of 69,660" position indicator if useful, but no page N/697.

### DG-2. Table columns aren't sortable
Sort is a separate dropdown; the data grid's own headers do nothing. Power users of a 20-column grid expect **click-header-to-sort** (with a direction caret) and shift-click multi-sort.
**Fix:** make headers sortable with visible sort state; keep the dropdown as a secondary/creator-friendly path.

### WF-1. No job/progress center for concurrent background work
Indexing, migration, batch-fix, dedup can run **at once**, but the only surface is a one-line log dock. There's nowhere to see *what's running*, progress, or **cancel**.
**Fix:** a **Jobs tray** (dropdown/panel) listing active + queued jobs with per-job progress, pause/cancel, and errors. The log dock becomes its collapsed summary.

### WF-3. "Propose, never auto" has no unified review queue
The core safety principle scatters proposals across Tiering, Dupes, Health with no single place they accumulate. Pending migrations/dedup/fixes can get lost, defeating the propose-first model.
**Fix:** a **Proposals / Review inbox** (its own icon-nav item + a badge) where every pending action queues for approval — one place to review and batch-approve/reject.

### WF-2. Selection across a 69K virtualized list is unmodeled
The ops bar hardcodes "3 selected." With virtual scroll + filters, we need **"select all N matching filter"** vs "select visible," a persistent count across scroll, and a clear "clear selection."
**Fix:** add a select-all-matching affordance ("Select all 12,431 matching"), persistent selection state, and a selection summary chip.

### IA-2. Redundant homes for the same feature
- **Var detail** exists as both a side panel *and* a full modal — when is which used?
- **Maintenance/dependency-analysis** actions live in the Library rail *and* as top-level screens (Health, Missing, Tiering, Dupes).
- **preset / AddonPackages-profile / collection** are three overlapping grouping concepts.
**Fix:** one canonical home per feature. Detail = expandable side panel with a "full view" that *replaces the center* (not a separate modal). Rail "Maintenance/Analysis" links should *navigate to* the top-level screens, not duplicate them. See MC-1 for grouping.

### MC-1. preset vs profile vs collection is ambiguous
Users must instantly grasp the difference. Our model: a **loading preset = the AddonPackages profile** (an activatable var-set, 1:1); a **collection/tag = organization only** (not activatable). The UI currently surfaces all three separately (rail collections + Presets screen + a separate "AddonPackages profile" dropdown in the older main-window).
**Fix:** collapse to two concepts in the UI — **Loading presets** (activatable, = profiles; drop the separate "profile" wording) and **Tags/Collections** (organize only). Never show "profile" as a distinct user concept.

### A11Y-1. Icon-only navigation is poor for learnability and accessibility
11 icon-only rail items (hover-tooltip only) are hard to learn and weak for keyboard/screen-reader users; the icon set also mixes emoji (🌡) with glyphs.
**Fix:** a **labeled rail** (expandable, or wide-with-labels by default) and **one coherent line-icon family**; add `aria-label`s regardless.

---

## 🟠 Medium

### IA-1. 11 top-level screens is a lot; some overlap
Tiering, Duplicates & Reclaim, Analytics (and stale) are all "storage optimization"; Health and Missing are both "problems to fix."
**Fix:** group the rail into sections — **Browse** (Library) · **Optimize** (Tiering, Duplicates/Reclaim, Analytics) · **Problems** (Health, Missing, + the Proposals inbox) · **System** (Repositories, Trash/Backup, History, Settings). Fewer top-level jumps.

### IA-3. No overview/dashboard — the mission isn't visible on open
The app opens on *browse*, but the differentiator ("we keep your SSD holding the right stuff, here's what needs attention, here's your space") has no at-a-glance home.
**Fix:** a compact **Dashboard/Home** landing (or a status header on Library): storage-by-tier, misplaced/reclaimable totals, problems needing attention, recent activity. Makes the value proposition legible in 3 seconds.

### WF-4. No undo after reversible destructive actions
Delete→trash is reversible, but there's no post-action **"Undo"** toast — users won't trust it without one.
**Fix:** toast on every reversible action: "Moved 2 to trash · Undo".

### WF-5. Large-batch guardrails absent
The old tool capped install (500) / delete (50). Huge selections need confirmation/chunking feedback.
**Fix:** confirm + progress for large batches; surface any cap.

### DG-3 / DG-4. No column chooser; no grid keyboard nav
15+ column grid needs show/hide/reorder/resize; power users need arrow/space/shift-select in the grid itself.
**Fix:** column chooser + full keyboard grid navigation.

### VS-2. No empty / loading / partial / error states
Not shown: mid-index partial catalog, offline-repo *unavailable* vars, zero-results, a failed operation. These are where apps feel broken.
**Fix:** design each state explicitly (skeleton rows while indexing, greyed unavailable rows, empty-state with next action, inline error with retry).

### VS-3. Color-only encoding for temperature/state
Hot/warm/cold is a bare dot; some states rely on color alone — a colorblindness risk.
**Fix:** pair color with shape/letter (H/W/C or an icon), not color alone.

---

## 🟢 Low / polish

- **VS-1.** Unify the icon set (no emoji among glyphs).
- **DG-5.** Count-column abbreviations (Sc Lk Cl…) are cryptic — add a legend or a compact content mini-bar; keep tooltips.
- **VS-4.** Verify the dense grid isn't noisy with temp-dot + tier-chip + state-pill + install-dot all at once; consolidate if so.
- **A11Y-3.** Verify `--text-lo` contrast hits WCAG AA on dense backgrounds.
- Command palette (Ctrl-K) is referenced but not built; toasts/notifications not shown.

---

## Top priorities (the short list)
1. **DG-1** — kill the pager, commit to virtual scroll (architecture consistency).
2. **WF-3 + WF-1** — a **Proposals inbox** and a **Jobs tray** (the propose-first, background-heavy model needs both).
3. **DG-2** — sortable columns.
4. **MC-1 + IA-2** — collapse preset/profile/collection; remove redundant detail/maintenance homes.
5. **A11Y-1 + IA-1** — labeled, grouped, single-icon-family navigation.
6. **WF-2** — select-all-matching.
7. **IA-3** — a Dashboard/Home so the mission is visible on open.

## Decisions for you
1. **Add a Dashboard/Home** as the landing screen (recommended), or keep Library as home?
2. **Add a Proposals/Review inbox** as a first-class screen (recommended) — yes?
3. **Labeled rail** (wider, learnable) vs. keep icon-only?
4. **Group the 11 screens** into Browse / Optimize / Problems / System sections?

---

*Next: fold the high-priority fixes into the prototype (or the ones you pick), then it's build-ready for Slice 1.*
