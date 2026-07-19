using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace VarVault.App.Controls;

/// <summary>
/// SC-10 · Toast (prototype `showToast`): a transient message with an optional Undo action and
/// auto-dismiss. The dismiss delay is injectable so the timeout is deterministically testable.
/// (16-checklist SC-10.)
/// </summary>
public class Toast : TemplatedControl
{
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromMilliseconds(4500);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private CancellationTokenSource? _dismissCts;
    private Action? _undo;

    public Toast() : this(Task.Delay) { }

    public Toast(Func<TimeSpan, CancellationToken, Task> delay) => _delay = delay;

    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<Toast, string?>(nameof(Message));

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<Toast, bool>(nameof(IsOpen));

    public static readonly StyledProperty<bool> HasUndoProperty =
        AvaloniaProperty.Register<Toast, bool>(nameof(HasUndo));

    public string? Message { get => GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public bool HasUndo { get => GetValue(HasUndoProperty); set => SetValue(HasUndoProperty, value); }

    /// <summary>The pending auto-dismiss task (exposed for tests to await settle).</summary>
    public Task? PendingDismiss { get; private set; }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<Button>("PART_Undo") is { } undo)
            undo.Click += (_, _) => Undo();
    }

    /// <summary>Show a message, optionally with an Undo action; auto-dismisses after the duration.</summary>
    public void Show(string message, Action? undo = null)
    {
        Message = message;
        _undo = undo;
        HasUndo = undo is not null;
        IsOpen = true;

        _dismissCts?.Cancel();
        var cts = _dismissCts = new CancellationTokenSource();
        PendingDismiss = DismissAfterDelayAsync(cts.Token);
    }

    private async Task DismissAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await _delay(DefaultDuration, token).ConfigureAwait(true);
            Dismiss();
        }
        catch (OperationCanceledException)
        {
            // superseded by a new toast or an explicit undo/dismiss
        }
    }

    /// <summary>Invoke the Undo action and hide.</summary>
    public void Undo()
    {
        _dismissCts?.Cancel();
        _undo?.Invoke();
        Dismiss();
    }

    public void Dismiss() => IsOpen = false;
}
