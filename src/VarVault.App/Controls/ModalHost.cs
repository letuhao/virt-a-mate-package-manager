using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace VarVault.App.Controls;

/// <summary>
/// SC-9 · Modal host / dialog base (prototype <c>.modal-bg</c>/<c>.modal</c>): a full-overlay backdrop + a
/// centered panel with header (title + ×), body (<see cref="ContentControl.Content"/>), and
/// <see cref="Footer"/>. One host; dialogs are its content. Esc and backdrop-click close; focus moves into
/// the dialog on open. (16-checklist SC-9.)
/// </summary>
public class ModalHost : ContentControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<ModalHost, bool>(nameof(IsOpen), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ModalHost, string?>(nameof(Title));

    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<ModalHost, object?>(nameof(Footer));

    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<Border>("PART_Backdrop") is { } backdrop)
            backdrop.PointerPressed += (_, args) =>
            {
                // Only a click on the backdrop itself (not the dialog) dismisses.
                if (ReferenceEquals(args.Source, backdrop))
                    Close();
            };
        if (e.NameScope.Find<Button>("PART_Close") is { } close)
            close.Click += (_, _) => Close();
        if (IsOpen)
            FocusDialog(e.NameScope);
    }

    private Border? _dialog;

    public void Close() => IsOpen = false;
    public void Open() => IsOpen = true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (IsOpen && e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void FocusDialog(INameScope scope)
    {
        _dialog = scope.Find<Border>("PART_Dialog");
        _dialog?.Focus();
    }
}
