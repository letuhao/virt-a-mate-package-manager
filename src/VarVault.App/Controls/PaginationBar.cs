using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Collections;

namespace VarVault.App.Controls;

/// <summary>Shared numbered pagination footer for secondary large-list surfaces.</summary>
public class PaginationBar : TemplatedControl
{
    private readonly AvaloniaList<int> _visiblePages = [];

    public static readonly StyledProperty<int> PageNumberProperty =
        AvaloniaProperty.Register<PaginationBar, int>(nameof(PageNumber), 1);

    public static readonly StyledProperty<int> PageCountProperty =
        AvaloniaProperty.Register<PaginationBar, int>(nameof(PageCount), 1);

    public static readonly StyledProperty<int> PageSizeProperty =
        AvaloniaProperty.Register<PaginationBar, int>(nameof(PageSize), 50);

    public static readonly StyledProperty<int> TotalCountProperty =
        AvaloniaProperty.Register<PaginationBar, int>(nameof(TotalCount));

    public static readonly StyledProperty<string> SummaryLabelProperty =
        AvaloniaProperty.Register<PaginationBar, string>(nameof(SummaryLabel), "0 results");

    public static readonly StyledProperty<ICommand?> PreviousCommandProperty =
        AvaloniaProperty.Register<PaginationBar, ICommand?>(nameof(PreviousCommand));

    public static readonly StyledProperty<ICommand?> NextCommandProperty =
        AvaloniaProperty.Register<PaginationBar, ICommand?>(nameof(NextCommand));

    public static readonly StyledProperty<ICommand?> GoToPageCommandProperty =
        AvaloniaProperty.Register<PaginationBar, ICommand?>(nameof(GoToPageCommand));

    public static readonly StyledProperty<ICommand?> PageSizeChangedCommandProperty =
        AvaloniaProperty.Register<PaginationBar, ICommand?>(nameof(PageSizeChangedCommand));

    public static readonly StyledProperty<IReadOnlyList<int>> PageSizeOptionsProperty =
        AvaloniaProperty.Register<PaginationBar, IReadOnlyList<int>>(nameof(PageSizeOptions), [25, 50, 100]);

    public static readonly DirectProperty<PaginationBar, IReadOnlyList<int>> VisiblePagesProperty =
        AvaloniaProperty.RegisterDirect<PaginationBar, IReadOnlyList<int>>(
            nameof(VisiblePages), o => o.VisiblePages);

    public int PageNumber { get => GetValue(PageNumberProperty); set => SetValue(PageNumberProperty, value); }
    public int PageCount { get => GetValue(PageCountProperty); set => SetValue(PageCountProperty, value); }
    public int PageSize { get => GetValue(PageSizeProperty); set => SetValue(PageSizeProperty, value); }
    public int TotalCount { get => GetValue(TotalCountProperty); set => SetValue(TotalCountProperty, value); }
    public string SummaryLabel { get => GetValue(SummaryLabelProperty); set => SetValue(SummaryLabelProperty, value); }
    public ICommand? PreviousCommand { get => GetValue(PreviousCommandProperty); set => SetValue(PreviousCommandProperty, value); }
    public ICommand? NextCommand { get => GetValue(NextCommandProperty); set => SetValue(NextCommandProperty, value); }
    public ICommand? GoToPageCommand { get => GetValue(GoToPageCommandProperty); set => SetValue(GoToPageCommandProperty, value); }
    public ICommand? PageSizeChangedCommand { get => GetValue(PageSizeChangedCommandProperty); set => SetValue(PageSizeChangedCommandProperty, value); }
    public IReadOnlyList<int> PageSizeOptions { get => GetValue(PageSizeOptionsProperty); set => SetValue(PageSizeOptionsProperty, value); }
    public IReadOnlyList<int> VisiblePages => _visiblePages;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PageNumberProperty || change.Property == PageCountProperty)
            UpdateVisiblePages();
    }

    private void UpdateVisiblePages()
    {
        _visiblePages.Clear();
        var pageCount = Math.Max(1, PageCount);
        var page = Math.Clamp(PageNumber, 1, pageCount);
        var start = Math.Max(1, page - 2);
        var end = Math.Min(pageCount, start + 4);
        start = Math.Max(1, end - 4);
        for (var i = start; i <= end; i++)
            _visiblePages.Add(i);
        RaisePropertyChanged(VisiblePagesProperty, null, _visiblePages);
    }
}
