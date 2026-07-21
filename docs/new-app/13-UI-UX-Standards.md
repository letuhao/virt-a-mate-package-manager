# 13 — UI/UX Standards

Rules for the Avalonia presentation layer. Complements the engineering standards ([12](./12-Engineering-Standards.md)) and the interactive drafts in [`mockups/`](./mockups/prototype.html). Stack: **Avalonia 12 + TreeDataGrid**, **CommunityToolkit.Mvvm**, MVVM, DI via the Host.

## 1. MVVM structure
- **Strict MVVM.** Views (`.axaml` + minimal code-behind) bind to ViewModels; ViewModels never reference Views or Avalonia controls. No business logic in code-behind.
- ViewModels use **CommunityToolkit.Mvvm**: `ObservableObject` base, `[ObservableProperty]` for bindable state, `[RelayCommand]` for actions (async commands return `Task`). Mark VM classes `partial`.
  ```csharp
  public sealed partial class LibraryViewModel(IIndexingService indexing, IUiDispatcher ui) : ViewModelBase
  {
      [ObservableProperty] private string _search = "";
      [RelayCommand] private async Task ReindexAsync(CancellationToken ct) { … }
  }
  ```
- **Constructor DI** for services (resolved from the Host container). No service locator, no `new`-ing services in VMs. VMs are registered transient; a lightweight `ViewLocator` maps `FooViewModel` → `FooView`.
- Naming: `FooView`/`FooViewModel`, dialogs `FooDialog`/`FooDialogViewModel`, `ViewModelBase` (a thin `ObservableObject` with shared plumbing).

## 2. Threading in the UI
- The UI thread is sacred. **All service/background calls are async**; commands `await` them. Never block the UI thread (no `.Result`/`.Wait()`).
- Long work goes to **`IJobQueue`**; the VM observes the `JobHandle` (progress + cancel) and marshals updates via **`IUiDispatcher`** (the Avalonia impl posts to `Dispatcher.UIThread`).
- Bind progress/state to observable properties; update them on the UI thread only.

## 3. The big data surfaces
- The library grid uses **TreeDataGrid** (virtualized) bound to the materialized read model via an **`OrderedSnapshot`** — O(1) scrollbar, no paging. The gallery is likewise virtualized with a **bounded thumbnail cache** and scroll-ahead prefetch; thumbnails decode **off the UI thread**.
- Never materialize the full 70k-row list into VM collections. Query page/slice on demand.
- Every potentially unbounded **secondary** list (missing deps, duplicates, integrity issues, stale versions, trash, proposals, preset members, package-detail tabs, creator analytics, activity history, import review) uses **numbered paging** with `Previous` / page numbers / `Next` and a page-size selector of **25 / 50 / 100**. Default = **50**.
- Reset secondary lists to page 1 when search/filter/sort changes; preserve page per tab for the current session; cancel stale requests; after mutations, reload and clamp to the last valid page.
- Allowed exceptions must stay explicit: the primary **Library** surface keeps the sealed A9 virtualized ordered snapshot, and **bounded summaries** (repositories, profiles, placement policy, encoding group summaries, backups, jobs, dashboard recent items, small cards) stay unpaged by design.
- Column sort is click-header with a direction indicator (not only the sort dropdown).

## 4. State: every surface handles all states
Design and implement **empty / loading / partial / error / offline** for every data view (a gap flagged in [08-UI-Design-Review](./08-UI-Design-Review.md)):
- **Loading** — skeleton rows/tiles, never a frozen blank.
- **Partial** (mid-index) — show what's indexed with a "still indexing" affordance.
- **Offline repo** — its rows render greyed/"unavailable", never removed.
- **Empty** — a message + the next action (e.g. "Add a repository").
- **Error** — inline, specific, with a retry; never a silent failure or a raw exception.

## 5. Safety-first UX (irreplaceable data)
- Destructive actions confirm, show the **reverse-dependency impact** and **single-copy protection**, and route to **trash** (recoverable) with an **Undo toast**.
- "Propose, never auto" — migration/dedup/fix land in the **Proposals inbox** for approval; nothing destructive runs unattended.
- Long operations are cancellable and show progress in the **Jobs tray**.

## 6. Design tokens (canonical — from the prototype)
Define once as Avalonia resources (light + dark); components reference tokens, never raw hex. Encode temperature/state by **shape+text as well as color** (colorblind-safe). Meet **WCAG AA** contrast.

**Dark (default)** · **Light** in parentheses:
| Token | Dark | Light |
|---|---|---|
| `bg.0/1/2/3` | `#0d0f13 / #15181f / #1c2029 / #232833` | `#eef0f4 / #f7f8fb / #ffffff / #f0f2f6` |
| `border` / `border.hi` | `#2b313d` / `#3a414f` | `#dde1e9` / `#c7ccd8` |
| `text.hi/mid/lo` | `#e7ebf2 / #aab3c2 / #6b7484` | `#161a22 / #4a5265 / #8a93a4` |
| `accent` (interactive/selection/focus) | `#7c86f0` | `#5a63d8` |
| semantic `good / warn / crit` | `#3ddc97 / #f4b740 / #fb6f84` | `#0f9d63 / #b9791a / #d63d55` |
| temperature `hot / warm / cold` | `#f2924e / #d9b16a / #6aa2c8` | `#d1732e / #a5833f / #4d7ea6` |

- **Accent is orthogonal** to the semantic (good/warn/crit) and temperature scales — don't reuse it for state.
- **Type:** system UI stack (Segoe UI on Windows) for UI; **monospace + tabular figures** for all numeric/data cells (sizes, versions, counts, hashes).
- **Spacing/shape:** dense pro-tool rhythm; radii ~5–7px; consistent gaps via layout, not per-element margins.
- **Icons:** one coherent line-icon family (no emoji as UI markers).

## 7. Accessibility
- **Labeled navigation** (not icon-only); every interactive control has an accessible name.
- Visible **focus states**; full **keyboard navigation** of the grid (arrows/space/shift-select) and dialogs; `Esc` closes overlays.
- State never encoded by **color alone** — pair with icon/letter/text.
- Respect reduced-motion; verify contrast in both themes.

## 8. Copy & interaction
- Write from the user's side: name things by what they are ("Loading presets", not "profiles"). Buttons say exactly what happens ("Fix encoding → UTF-8"), toasts confirm ("Moved 2 to trash · Undo").
- Errors say what went wrong and how to fix it — no apologies, no vagueness.
- Remember view state (filters, sort, columns, last screen) across sessions.

---

*Sources:* [Avalonia MVVM patterns](https://docs.avaloniaui.net/docs/how-to/mvvm-how-to) · [Avalonia DI](https://docs.avaloniaui.net/docs/guides/implementation-guides/how-to-implement-dependency-injection) · [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/).
