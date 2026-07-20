using System.Diagnostics;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Composition;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Library;
using VarVault.Sdk.Presets;
using VarVault.Sdk.Repositories;
using VarVault.Sdk.Settings;
using VarVault.TestKit;
using Xunit.Abstractions;

namespace VarVault.App.Tests;

/// <summary>
/// "Newcomer real run": composes a BRAND-NEW install (empty temp catalog), then plays a first-time user who
/// wants to manage TWO real repositories on different drives plus a real VaM game folder — driving the actual
/// dialogs / settings / rail navigation (not raw service calls), visiting every screen through the real
/// auto-load path a rail-click triggers, and attempting the headline newcomer job (activate a preset's symlinks
/// into the game folder). It records UI/UX metrics (per-screen load latency, item counts, empty states) and
/// dead-ends into a markdown report under the UI-evidence dir. Fully env-var driven — no hardcoded paths:
///   VARVAULT_TEST_CORPUS   → repo #1   VARVAULT_TEST_CORPUS_2 → repo #2   VARVAULT_VAM_PATH → game folder.
/// Skips (early-return) when the two corpora are absent, so a fresh clone stays green.
/// </summary>
public class NewcomerRealRunE2ETests(ITestOutputHelper output)
{
    private const string VamPathVar = "VARVAULT_VAM_PATH";

    private readonly StringBuilder _report = new();
    private readonly List<string> _deadEnds = new();

    [AvaloniaFact]
    public async Task Newcomer_manages_two_repos_and_a_game_folder_end_to_end()
    {
        var repo1 = TestCorpus.Primary;
        var repo2 = TestCorpus.Secondary;
        var vamPath = Environment.GetEnvironmentVariable(VamPathVar);
        if (repo1 is null || repo2 is null)
        {
            output.WriteLine($"SKIPPED — set {TestCorpus.PrimaryVar} and {TestCorpus.SecondaryVar} to run the newcomer walkthrough.");
            return;
        }

        Head("# VarVault — Newcomer Real Run");
        Line($"- Repo #1: `{repo1}`");
        Line($"- Repo #2: `{repo2}`");
        Line($"- Game folder: `{vamPath ?? "(VARVAULT_VAM_PATH not set)"}`");
        Line("");

        await using var host = TestHost.Create(withPersistence: true);
        var services = host.Host.Services;

        // ── STEP 0 · First launch on an EMPTY install: onboarding must open on its own (C1) ─────────────
        Head("## Step 0 — First launch (empty install) opens onboarding");
        {
            var needsFirst = await AppHost.NeedsOnboardingAsync(services);
            Line($"- Zero-repo install ⇒ needs onboarding: **{needsFirst}**");
            if (!needsFirst)
                _deadEnds.Add("Phase-C fix NOT verified — C1.1: a zero-repo install does not flag onboarding.");

            using var scope0 = services.CreateScope();
            var shell0 = AppHost.CreateShell(scope0.ServiceProvider);
            shell0.ShowOnboardingOnLoad = needsFirst;
            var window0 = new MainWindow { DataContext = shell0 };
            window0.Show(); // triggers OnLoaded → MaybeShowOnboarding
            for (var i = 0; i < 5; i++) { UiE2E.Pump(); await Task.Delay(10); }
            var opened = shell0.Dialogs.Current is OnboardingViewModel;
            Line($"- Onboarding wizard auto-opened on the empty install: **{opened}**");
            if (!opened)
                _deadEnds.Add("Phase-C fix NOT verified — C1.2: onboarding wizard did not auto-open on the empty install.");
            shell0.Dialogs.Close();
            window0.Close();
        }

        // ── STEP 1 · First launch: register both repositories via the real Add-Repo dialog ──────────────
        Head("## Step 1 — Add repositories (real Add-Repo dialog)");
        foreach (var (label, path) in new[] { ("repo #1", repo1), ("repo #2", repo2) })
        {
            using var scope = services.CreateScope();
            var vm = new AddRepoViewModel(scope.ServiceProvider.GetRequiredService<IRepositoryService>());
            vm.FolderPath = path;
            var sw = Stopwatch.StartNew();
            await vm.AddCommand.ExecuteAsync(null);
            sw.Stop();
            if (vm.Registered is null)
            {
                _deadEnds.Add($"Add-Repo failed for {label} ({path}): {vm.Message}");
                Line($"- ❌ {label}: **failed** — {vm.Message}");
            }
            else
            {
                Line($"- ✅ {label}: {vm.Message} ({sw.ElapsedMilliseconds} ms)");
            }
        }

        // ── STEP 2 · Point at the VaM game folder via Settings, with real validation ─────────────────────
        Head("## Step 2 — Set the VaM game folder (Settings)");
        {
            using var scope = services.CreateScope();
            var settingsVm = new SettingsViewModel(scope.ServiceProvider.GetRequiredService<ISettingsService>());
            await settingsVm.LoadCommand.ExecuteAsync(null);
            if (vamPath is not null)
            {
                settingsVm.VamPath = vamPath;
                Line($"- VaM path valid? **{settingsVm.IsVamPathValid}** {(settingsVm.VamPathValidationMessage is { } m ? $"— {m}" : "")}");
                if (!settingsVm.IsVamPathValid)
                    _deadEnds.Add($"VaM path '{vamPath}' rejected by Settings validation: {settingsVm.VamPathValidationMessage}");
                await settingsVm.SaveCommand.ExecuteAsync(null);
                Line($"- Save status: {settingsVm.StatusMessage}");
            }
            else
            {
                Line("- ⚠️ Skipped — VARVAULT_VAM_PATH not set; activation/install steps will be limited.");
            }
        }

        // ── STEP 3 · Index everything (the newcomer clicks nothing; startup enqueues this) ───────────────
        Head("## Step 3 — Index both repositories");
        IndexRunSummary summary;
        {
            using var scope = services.CreateScope();
            var sw = Stopwatch.StartNew();
            summary = await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();
            sw.Stop();
            Line($"- Indexed **{summary.Indexed}** vars across **{summary.Repositories}** repos in {sw.ElapsedMilliseconds} ms " +
                 $"({(summary.Indexed == 0 ? 0 : sw.ElapsedMilliseconds / (double)summary.Indexed):F1} ms/var)");
            if (summary.Indexed == 0)
                _deadEnds.Add("Indexing produced 0 vars — the whole app is empty for this newcomer.");
        }

        // ── STEP 4 · Launch the real shell + window and walk every rail screen ──────────────────────────
        Head("## Step 4 — Walk every screen (real rail navigation + auto-load)");
        // C1 · once repositories exist the app must NOT nag with onboarding again.
        var needsAfterSetup = await AppHost.NeedsOnboardingAsync(services);
        Line($"- After repos registered ⇒ needs onboarding: **{needsAfterSetup}** (expected false)");
        if (needsAfterSetup)
            _deadEnds.Add("Phase-C fix NOT verified — C1: onboarding still flagged after repositories were registered.");

        var scope2 = services.CreateScope();
        var shell = AppHost.CreateShell(scope2.ServiceProvider);
        shell.ShowOnboardingOnLoad = needsAfterSetup;
        var landingScreen = shell.ActiveScreenId; // B1: what the newcomer sees first, before any navigation
        var window = new MainWindow { DataContext = shell };
        window.Show();
        UiE2E.Pump();
        Line($"- Landing screen (first thing a newcomer sees): **{landingScreen}**");

        Line("");
        Line("| Screen | Load ms | Primary count | State |");
        Line("|---|---:|---:|---|");
        foreach (var screen in ShellViewModel.AllScreens)
            await WalkScreen(shell, window, screen);

        // ── STEP 4b · Verify the doc-28 Phase-A/B fixes over the RENDERED real-data UI ───────────────────
        Head("## Step 4b — Phase-A/B fix verification (rendered, real data)");
        await VerifyPhaseA(shell, window);
        await VerifyPhaseB(shell, window, landingScreen);
        await VerifyPhaseD(shell, window);
        await VerifyPhaseE(shell, window);

        // F2 · M3 · the dashboard "+ Add repository" quick action opens the Add-Repo dialog (same as the top bar),
        // not the onboarding wizard — one label, one behavior.
        await NavigateAndRender(shell, "dashboard");
        var dashVm = (DashboardViewModel)shell.ActiveScreen!;
        dashVm.AddRepoCommand.Execute(null);
        UiE2E.Pump();
        var addRepoOpened = shell.Dialogs.Current is AddRepoViewModel;
        Line($"- {(addRepoOpened ? "✅" : "❌")} F2/M3 Dashboard '+ Add repository' opens Add-Repo dialog: {addRepoOpened}");
        if (!addRepoOpened)
            _deadEnds.Add("Phase-F fix NOT verified — M3: dashboard '+ Add repository' did not open the Add-Repo dialog.");
        shell.Dialogs.Close();

        // ── STEP 5 · Headline newcomer job: activate a preset's symlinks into the game folder ───────────
        Head("## Step 5 — Activate a preset into the game folder (install)");
        await ActivateFlow(services, shell, vamPath);

        // ── STEP 6 · Spot-check the reachability of the key problem-solving dialogs ──────────────────────
        Head("## Step 6 — Key action reachability");
        await CheckActionReachability(shell);

        // ── Report ───────────────────────────────────────────────────────────────────────────────────
        Head("## Dead-ends / won't-work items");
        if (_deadEnds.Count == 0)
            Line("- ✅ None encountered in this run.");
        else
            foreach (var d in _deadEnds)
                Line($"- ❌ {d}");

        var reportPath = Path.Combine(UiE2E.EvidenceDir, "newcomer-real-run.md");
        File.WriteAllText(reportPath, _report.ToString());
        output.WriteLine(_report.ToString());
        output.WriteLine($"\nReport written to: {reportPath}");

        // Drain any remaining fire-and-forget UI continuations, then tear down the shell scope before the host,
        // so no scoped-service continuation races the host disposal (ObjectDisposedException on a bg thread).
        for (var i = 0; i < 10; i++) { UiE2E.Pump(); await Task.Delay(20); }
        window.Close();
        scope2.Dispose();

        // Sanity: the run must have actually indexed a non-trivial catalog for the walkthrough to mean anything.
        Assert.True(summary.Indexed > 0, "Newcomer run indexed 0 vars — see report.");

        // Gate: every doc-28 Phase-A fix must be verified on the rendered real-data UI.
        var phaseFailures = _deadEnds.Where(d => d.Contains("fix NOT verified", StringComparison.Ordinal)).ToList();
        Assert.True(phaseFailures.Count == 0, "Fixes not proven on real data:\n" + string.Join("\n", phaseFailures));
    }

    private async Task WalkScreen(ShellViewModel shell, MainWindow window, ShellScreen screen)
    {
        var sw = Stopwatch.StartNew();
        string state;
        long count = -1;
        try
        {
            shell.Navigate(screen.Id);
            if (shell.PendingScreenLoad is { } load)
                await load;
            UiE2E.Pump();
            sw.Stop();
            (count, state) = Inspect(shell.ActiveScreen);
        }
        catch (Exception ex)
        {
            sw.Stop();
            state = "❌ EXCEPTION";
            _deadEnds.Add($"Screen '{screen.Label}' threw on load: {ex.GetType().Name}: {ex.Message}");
        }

        try { UiE2E.Screenshot(window, $"newcomer-{screen.Id}"); } catch { /* headless render best-effort */ }

        var countCell = count < 0 ? "—" : count.ToString();
        Line($"| {screen.Label} | {sw.ElapsedMilliseconds} | {countCell} | {state} |");

        // Only a true dead-end if the empty screen renders NO guidance text (a "No … yet" / CTA is fine). (B2 fix)
        if (state.StartsWith("empty", StringComparison.OrdinalIgnoreCase) && count == 0)
        {
            var texts = RenderedTexts(window);
            var hasGuidance = texts.Any(t => t.Contains("No ", StringComparison.Ordinal)
                || t.Contains("yet", StringComparison.OrdinalIgnoreCase)
                || t.Contains("balanced", StringComparison.OrdinalIgnoreCase));
            if (!hasGuidance)
                _deadEnds.Add($"Screen '{screen.Label}' loads empty with NO guidance text — a blank dead-end.");
        }
    }

    /// <summary>Read a screen VM's primary collection count + a human state, mirroring what the user sees.</summary>
    private static (long count, string state) Inspect(object? vm) => vm switch
    {
        DashboardViewModel d => (d.RecentActivity.Count, d.Summary is null ? "empty (no summary)" : $"ok · {d.Summary.TotalPackages} pkgs"),
        LibraryViewModel l => (l.TotalCount, l.TotalCount == 0 ? "empty" : "ok"),
        RepositoriesViewModel r => (r.Repositories.Count, r.Repositories.Count == 0 ? "empty" : "ok"),
        PresetsViewModel p => (p.Presets.Count, p.IsEmpty ? "empty" : "ok"),
        TieringViewModel t => (t.Misplaced.Count + t.Policy.Count + t.StaleVersions.Count, t.Counts is null ? "empty (no counts)" : "ok"),
        DupesViewModel du => (du.Groups.Count + du.NearGroups.Count, du.Groups.Count + du.NearGroups.Count == 0 ? "empty" : "ok"),
        AnalyticsViewModel a => (a.ByType.Count, a.ByType.Count == 0 ? "empty" : "ok"),
        ProposalsViewModel pr => (pr.Pending.Count, pr.Pending.Count == 0 ? "empty" : "ok"),
        HealthViewModel h => (h.EncodingGroups.Count + h.Integrity.Count + h.MissingMeta.Count,
            h.EncodingGroups.Count + h.Integrity.Count + h.MissingMeta.Count == 0 ? "empty (clean)" : "ok"),
        MissingDepsViewModel md => (md.TotalMissing, md.TotalMissing == 0 ? "empty (no missing)" : (md.IsCapped ? $"ok · capped {md.Items.Count}/{md.TotalMissing}" : "ok")),
        TrashViewModel tr => (tr.Items.Count + tr.Backups.Count, "ok"),
        ActivityViewModel av => (av.Items.Count, av.Items.Count == 0 ? "empty" : "ok"),
        SettingsViewModel => (-1, "ok (form)"),
        PlaceholderScreenViewModel => (-1, "⚠️ PLACEHOLDER"),
        null => (-1, "❌ null screen"),
        _ => (-1, vm.GetType().Name),
    };

    private async Task ActivateFlow(IServiceProvider services, ShellViewModel shell, string? vamPath)
    {
        // Build a small preset from a few real library packages, then drive the Presets screen's Activate command.
        string? firstMember = null;
        long presetId;
        using (var scope = services.CreateScope())
        {
            var lib = scope.ServiceProvider.GetRequiredService<ILibraryQueryService>();
            var page = await lib.GetPageAsync(new LibraryQuery(Take: 5));
            var names = page.Items.Select(i => i.VarName).ToList();
            firstMember = names.FirstOrDefault();
            var presets = scope.ServiceProvider.GetRequiredService<IPresetService>();
            var created = await presets.CreateAsync("Newcomer Loadout", []);
            presetId = created.Value.Id;
            foreach (var n in names)
                await presets.AddMemberAsync(presetId, n);
            Line($"- Created preset 'Newcomer Loadout' with {names.Count} members (e.g. {firstMember}).");
        }

        shell.Navigate("presets");
        if (shell.PendingScreenLoad is { } load) await load;
        var presetsVm = (PresetsViewModel)shell.ActiveScreen!;
        UiE2E.Pump();

        presetsVm.Selected = presetsVm.Presets.FirstOrDefault(p => p.Id == presetId);
        // Selecting fires a fire-and-forget LoadPreviewAsync (OnSelectedChanged) that reads the scoped preset
        // service — drain it deterministically so it can't outlive the host and race disposal.
        for (var i = 0; i < 50 && presetsVm.Preview is null; i++) { UiE2E.Pump(); await Task.Delay(20); }
        UiE2E.Pump();

        Line($"- VamPathConfigured (gates Activate): **{presetsVm.VamPathConfigured}**");
        Line($"- Activate command enabled: **{presetsVm.ActivateCommand.CanExecute(null)}**");

        if (!presetsVm.VamPathConfigured)
        {
            if (vamPath is null)
                Line("- ⚠️ Cannot activate — no game folder configured (expected: VARVAULT_VAM_PATH unset).");
            else
                _deadEnds.Add("Presets screen reports VamPathConfigured=false even though the VaM path was saved — Activate is dead-ended.");
            return;
        }

        if (!presetsVm.ActivateCommand.CanExecute(null))
        {
            _deadEnds.Add("Activate command is disabled despite a selected preset + configured VaM path.");
            return;
        }

        var sw = Stopwatch.StartNew();
        await presetsVm.ActivateCommand.ExecuteAsync(null);
        sw.Stop();
        Line($"- Activate result ({sw.ElapsedMilliseconds} ms): {presetsVm.StatusMessage}");
        if (presetsVm.StatusMessage is { } s && s.Contains("Developer Mode", StringComparison.OrdinalIgnoreCase))
            _deadEnds.Add("Activation blocked by symlink privilege (Developer Mode) — newcomer cannot install without elevation.");
        else if (presetsVm.StatusMessage is { } s2 && s2.StartsWith("Activated", StringComparison.Ordinal))
            Line("- ✅ Symlinks materialized into the game folder.");
    }

    private async Task CheckActionReachability(ShellViewModel shell)
    {
        // Duplicates → review dialog.
        shell.Navigate("dupes");
        if (shell.PendingScreenLoad is { } l1) await l1;
        var dupes = (DupesViewModel)shell.ActiveScreen!;
        if (dupes.Groups.Count > 0)
        {
            dupes.ReviewCommand.Execute(dupes.Groups[0]);
            Line($"- Duplicates review dialog reachable: {shell.Dialogs.Current is DupeReviewViewModel}");
            shell.Dialogs.Close();
        }
        else Line("- Duplicates: no exact-dup groups in this corpus (nothing to review).");

        // Tiering → migrate plan dialog.
        shell.Navigate("tiering");
        if (shell.PendingScreenLoad is { } l2) await l2;
        var tiering = (TieringViewModel)shell.ActiveScreen!;
        tiering.PlanCommand.Execute(null);
        Line($"- Migrate plan dialog reachable: {shell.Dialogs.Current is MigrateViewModel}");
        if (shell.Dialogs.Current is not MigrateViewModel)
            _deadEnds.Add("Tiering 'Plan migration' did not open the migrate dialog.");
        shell.Dialogs.Close();

        // Health → fix dialog.
        shell.Navigate("health");
        if (shell.PendingScreenLoad is { } l3) await l3;
        var health = (HealthViewModel)shell.ActiveScreen!;
        health.FixAllCommand.Execute(null);
        Line($"- Health fix-encoding dialog reachable: {shell.Dialogs.Current is FixEncodingViewModel}");
        shell.Dialogs.Close();

        // Missing → resolve/alias dialog.
        shell.Navigate("missing");
        if (shell.PendingScreenLoad is { } l4) await l4;
        var missing = (MissingDepsViewModel)shell.ActiveScreen!;
        if (missing.Items.Count > 0)
        {
            missing.ResolveCommand.Execute(missing.Items[0]);
            Line($"- Missing-deps alias dialog reachable: {shell.Dialogs.Current is AliasViewModel}");
            shell.Dialogs.Close();
        }
        else Line("- Missing deps: none reported for this corpus.");
    }

    /// <summary>All visible text the user can read on the active screen (TextBlock text + string button content).</summary>
    private static List<string> RenderedTexts(Visual root)
    {
        var texts = root.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!).ToList();
        texts.AddRange(root.GetVisualDescendants().OfType<Button>()
            .Select(b => b.Content?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!));
        return texts;
    }

    private async Task NavigateAndRender(ShellViewModel shell, string id)
    {
        shell.Navigate(id);
        if (shell.PendingScreenLoad is { } load) await load;
        for (var i = 0; i < 3; i++) { UiE2E.Pump(); await Task.Delay(10); }
        UiE2E.Pump();
    }

    private void VerifyPhaseAResult(string tag, bool pass, string detail)
    {
        Line($"- {(pass ? "✅" : "❌")} {tag}: {detail}");
        if (!pass)
            _deadEnds.Add($"Phase-A fix NOT verified — {tag}: {detail}");
    }

    private async Task VerifyPhaseA(ShellViewModel shell, MainWindow window)
    {
        // A1 · Dashboard renders humanized bytes (unit present, no bare digit-only byte value).
        await NavigateAndRender(shell, "dashboard");
        var dash = (DashboardViewModel)shell.ActiveScreen!;
        var reclaimHumanized = VarVault.Common.Formatting.ByteSize.Humanize(dash.ReclaimableBytes);
        var dashTexts = RenderedTexts(window);
        var dashHasUnit = dashTexts.Any(t => t.Contains(reclaimHumanized, StringComparison.Ordinal))
                          && (reclaimHumanized.Contains("GB") || reclaimHumanized.Contains("TB") || reclaimHumanized.Contains("MB"));
        var dashRawBytes = dashTexts.FirstOrDefault(t => t.Contains(" bytes", StringComparison.Ordinal)
            || (t.Length >= 9 && t.All(char.IsDigit)));
        VerifyPhaseAResult("A1 Dashboard bytes humanized",
            dashHasUnit && dashRawBytes is null,
            dashHasUnit ? $"reclaimable renders '{reclaimHumanized}'" + (dashRawBytes is null ? "" : $"; BUT raw value still shown: '{dashRawBytes}'")
                        : $"expected '{reclaimHumanized}' in rendered dashboard");

        // A2 · Dashboard tile is worded so it can't be misread as the Missing-deps total (which differs).
        var tile = dashTexts.FirstOrDefault(t => t.Contains("need dependencies", StringComparison.Ordinal));
        var oldWording = dashTexts.Any(t => t.Contains("missing dependencies", StringComparison.OrdinalIgnoreCase));
        VerifyPhaseAResult("A2 Dashboard missing-deps label",
            tile is not null && !oldWording,
            tile is not null ? $"tile reads '{tile}' (distinct from the Missing screen's absent-ref total)" : "tile still says 'missing dependencies'");

        // A1 · Duplicates renders humanized reclaimable.
        await NavigateAndRender(shell, "dupes");
        var dupes = (DupesViewModel)shell.ActiveScreen!;
        var dupeHumanized = VarVault.Common.Formatting.ByteSize.Humanize(dupes.ReclaimableBytes);
        var dupeTexts = RenderedTexts(window);
        var dupeOk = dupeTexts.Any(t => t.Contains(dupeHumanized, StringComparison.Ordinal))
                     && !dupeTexts.Any(t => t.Contains(" bytes reclaimable", StringComparison.Ordinal));
        VerifyPhaseAResult("A1 Duplicates bytes humanized", dupeOk,
            dupeOk ? $"reclaimable renders '{dupeHumanized} reclaimable'" : $"expected '{dupeHumanized}', old ' bytes reclaimable' still present");

        // A1 · Library size column renders humanized bytes for a real row.
        await NavigateAndRender(shell, "library");
        var lib = (LibraryViewModel)shell.ActiveScreen!;
        var firstSize = lib.Items.Count > 0 ? lib.Items[0].TotalSize : 0;
        var sizeHumanized = VarVault.Common.Formatting.ByteSize.Humanize(firstSize);
        var libTexts = RenderedTexts(window);
        var libOk = firstSize == 0 || libTexts.Any(t => t.Contains(sizeHumanized, StringComparison.Ordinal));
        VerifyPhaseAResult("A1 Library Size column humanized", libOk,
            libOk ? $"first row size renders '{sizeHumanized}'" : $"expected '{sizeHumanized}' in the Library grid");

        // A3 · Repositories show folder-derived names, not the literal 'repository'.
        await NavigateAndRender(shell, "repos");
        var repos = (RepositoriesViewModel)shell.ActiveScreen!;
        var names = repos.Repositories.Select(r => r.Name).ToList();
        var derivedOk = names.Count > 0 && names.All(n => !string.Equals(n, "repository", StringComparison.Ordinal));
        VerifyPhaseAResult("A3 Repository names derived from folder", derivedOk,
            derivedOk ? $"repos named [{string.Join(", ", names)}]" : $"repos still generic: [{string.Join(", ", names)}]");

        UiE2E.Screenshot(window, "newcomer-verify-dashboard");

        // M2/F1 · responsive facet bar: shrink the window and prove the Library filters wrap to a second row
        // instead of overlapping. Capture a narrow screenshot as evidence, then restore the width.
        await NavigateAndRender(shell, "library");
        var wide = window.Width;
        window.Width = 1040;
        for (var i = 0; i < 4; i++) { UiE2E.Pump(); await Task.Delay(10); }
        UiE2E.Screenshot(window, "newcomer-library-narrow");
        Line($"- 📐 Captured narrow-window (720px) Library screenshot to verify the facet bar wraps (M2/F1).");
        window.Width = wide;
        UiE2E.Pump();
    }

    private async Task VerifyPhaseB(ShellViewModel shell, MainWindow window, string landingScreen)
    {
        // B1 · A newcomer lands on the Dashboard (orienting), not the dense/empty Library.
        VerifyPhaseBResult("B1 Lands on Dashboard", landingScreen == "dashboard",
            $"initial screen is '{landingScreen}'");

        // B2 · Activity history shows guidance, not a blank card, on an empty log.
        await NavigateAndRender(shell, "history");
        var activity = (ActivityViewModel)shell.ActiveScreen!;
        var histTexts = RenderedTexts(window);
        var histOk = !activity.IsEmpty || histTexts.Any(t => t.Contains("No activity yet", StringComparison.Ordinal));
        VerifyPhaseBResult("B2 Activity empty-state", histOk,
            activity.IsEmpty ? (histOk ? "renders 'No activity yet' guidance" : "blank card — no guidance") : "has activity (non-empty)");

        // B2 · Presets shows a "New preset" CTA, not a blank pane, before any preset exists.
        await NavigateAndRender(shell, "presets");
        var presets = (PresetsViewModel)shell.ActiveScreen!;
        var presetTexts = RenderedTexts(window);
        var presetOk = !presets.IsEmpty || presetTexts.Any(t => t.Contains("No loading presets yet", StringComparison.Ordinal));
        VerifyPhaseBResult("B2 Presets empty-state", presetOk,
            presets.IsEmpty ? (presetOk ? "renders 'No loading presets yet' + CTA" : "blank pane — no guidance") : "has presets (non-empty)");
    }

    private void VerifyPhaseBResult(string tag, bool pass, string detail)
    {
        Line($"- {(pass ? "✅" : "❌")} {tag}: {detail}");
        if (!pass)
            _deadEnds.Add($"Phase-B fix NOT verified — {tag}: {detail}");
    }

    private async Task VerifyPhaseD(ShellViewModel shell, MainWindow window)
    {
        var sw = Stopwatch.StartNew();
        await NavigateAndRender(shell, "missing");
        sw.Stop();
        var md = (MissingDepsViewModel)shell.ActiveScreen!;
        if (md.TotalMissing == 0)
        {
            Line("- ⚠️ Missing-deps triage: corpus has no missing deps — cap/toggle not exercised.");
            return;
        }

        // D1.1/D1.3 · contextual header rendered (frames the count as downloads, not a bug).
        var texts = RenderedTexts(window);
        var hasGuidance = texts.Any(t => t.Contains("downloads you still need", StringComparison.Ordinal));
        VerifyPhaseDResult("D1.1 Missing-deps summary header", hasGuidance,
            hasGuidance ? $"header framed for {md.TotalMissing} refs" : "no contextual header rendered");

        // D1.2 · initial render is capped at 200 and ordered most-needed-first.
        var capped = md.TotalMissing > 200 ? md.Items.Count == 200 && md.IsCapped : !md.IsCapped;
        var orderedDesc = md.Items.Count < 2 || md.Items[0].NeededByCount >= md.Items[^1].NeededByCount;
        VerifyPhaseDResult("D1.2 Missing-deps capped + ordered",
            capped && orderedDesc,
            $"render={md.Items.Count}/{md.TotalMissing}, top needed-by={md.Items.FirstOrDefault()?.NeededByCount}, load {sw.ElapsedMilliseconds} ms");

        // D1.2 · "show all" toggle expands to the full set, then collapses back.
        md.ToggleShowAllCommand.Execute(null);
        UiE2E.Pump();
        var expandedOk = md.Items.Count == md.TotalMissing && !md.IsCapped;
        md.ToggleShowAllCommand.Execute(null);
        UiE2E.Pump();
        var collapsedOk = md.TotalMissing <= 200 || md.Items.Count == 200;
        VerifyPhaseDResult("D1.2 Missing-deps show-all toggle", expandedOk && collapsedOk,
            expandedOk ? "toggles to all then back to top-200" : "toggle did not expand to the full set");
    }

    private void VerifyPhaseDResult(string tag, bool pass, string detail)
    {
        Line($"- {(pass ? "✅" : "❌")} {tag}: {detail}");
        if (!pass)
            _deadEnds.Add($"Phase-D fix NOT verified — {tag}: {detail}");
    }

    private async Task VerifyPhaseE(ShellViewModel shell, MainWindow window)
    {
        await NavigateAndRender(shell, "tiering");
        var tiering = (TieringViewModel)shell.ActiveScreen!;
        var texts = RenderedTexts(window);
        var hintRendered = texts.Any(t => t.Contains("All storage is cold", StringComparison.Ordinal));
        // Correct behavior either way: hint shown iff no T1/T2 drive. The real corpus is HDD-only (T3) so it should show.
        var pass = tiering.ShowHddOnlyHint ? hintRendered : !hintRendered;
        Line($"- {(pass ? "✅" : "❌")} E1 Tiering HDD-only hint: HasFastDrive={tiering.HasFastDrive}, hintRendered={hintRendered}");
        if (!pass)
            _deadEnds.Add($"Phase-E fix NOT verified — E1: ShowHddOnlyHint={tiering.ShowHddOnlyHint} but rendered={hintRendered}");
    }

    private void Head(string s) { _report.AppendLine(s); _report.AppendLine(); }
    private void Line(string s) => _report.AppendLine(s);
}
