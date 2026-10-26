using Bowen.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Bowen.WinUI.Controls;

/// <summary>
/// 分组字体选择器。原生 ComboBox 不支持分组标题，这里用 DropDownButton + 分组 ListView。
/// </summary>
public sealed partial class FontPicker : UserControl
{
    public static readonly DependencyProperty SelectedIndexProperty =
        DependencyProperty.Register(
            nameof(SelectedIndex),
            typeof(int),
            typeof(FontPicker),
            new PropertyMetadata(-1, OnSelectedIndexChanged));

    private readonly CollectionViewSource _groupedSource = new() { IsSourceGrouped = true };
    private IReadOnlyList<FontOption>? _itemsSource;
    private bool _updating;

    public FontPicker()
    {
        InitializeComponent();
        PickerFlyout.Opening += (_, _) =>
            FontList.Width = Math.Clamp(PickerButton.ActualWidth, 200, 320);
        PickerFlyout.Opened += (_, _) => DispatcherQueue.TryEnqueue(CenterSelectedItem);
    }

    /// <summary>扁平字体列表；下拉按 Group 分组显示，但 SelectedIndex 仍对应这个列表。</summary>
    public IReadOnlyList<FontOption>? ItemsSource
    {
        get => _itemsSource;
        set
        {
            if (ReferenceEquals(_itemsSource, value)) return;
            _itemsSource = (value ?? Array.Empty<FontOption>()).ToList();
            RebuildGroups();
        }
    }

    /// <summary>选中项在扁平列表里的下标；0 是「默认字体」。</summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    private static void OnSelectedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs args)
    {
        var picker = (FontPicker)d;
        if (!picker._updating) picker.UpdateSelection();
    }

    private void RebuildGroups()
    {
        var groups = _itemsSource!
            .GroupBy(option => option.Group)
            .OrderBy(group => group.Key)
            .Select(group => new FontGroupInfo(group))
            .ToList();

        _groupedSource.Source = groups;
        FontList.ItemsSource = _groupedSource.View;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        if (FontList is null || PickerButton is null) return;

        _updating = true;
        try
        {
            var item = _itemsSource is { } items && SelectedIndex >= 0 && SelectedIndex < items.Count
                ? items[SelectedIndex]
                : null;
            FontList.SelectedItem = item;
            PickerText.Text = item?.DisplayName ?? "默认字体";
        }
        finally
        {
            _updating = false;
        }
    }

    private void OnFontSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_updating || FontList.SelectedItem is not FontOption option) return;

        var index = IndexOf(option);
        if (index < 0) return;

        _updating = true;
        try
        {
            SetValue(SelectedIndexProperty, index);
        }
        finally
        {
            _updating = false;
        }

        PickerText.Text = option.DisplayName;
        PickerFlyout.Hide();
    }

    private int IndexOf(FontOption option)
    {
        if (_itemsSource is not { } items) return -1;
        for (var index = 0; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], option)) return index;
        }
        return -1;
    }

    /// <summary>把当前选中项滚到视口中间。列表两端时自然退回顶部或底部。</summary>
    private void CenterSelectedItem()
    {
        if (FontList.SelectedItem is not { } item) return;

        FontList.UpdateLayout();
        var container = FontList.ContainerFromItem(item) as ListViewItem;
        if (container is null)
        {
            FontList.ScrollIntoView(item);
            FontList.UpdateLayout();
            container = FontList.ContainerFromItem(item) as ListViewItem;
            if (container is null) return;
        }

        var scrollViewer = FindDescendant<ScrollViewer>(FontList);
        if (scrollViewer is null || scrollViewer.ViewportHeight <= 0) return;

        Point position;
        try
        {
            position = container.TransformToVisual(scrollViewer).TransformPoint(new Point(0, 0));
        }
        catch (ArgumentException)
        {
            return;
        }

        var target = scrollViewer.VerticalOffset + position.Y
                     - (scrollViewer.ViewportHeight - container.ActualHeight) / 2;
        target = Math.Clamp(target, 0, Math.Max(0, scrollViewer.ScrollableHeight));
        scrollViewer.ChangeView(null, target, null, disableAnimation: true);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) return match;
            var found = FindDescendant<T>(child);
            if (found is not null) return found;
        }
        return null;
    }
}

/// <summary>ListView 分组容器；Label 给 GroupStyle.HeaderTemplate 用。</summary>
public sealed class FontGroupInfo : List<FontOption>
{
    public FontGroupInfo(IEnumerable<FontOption> items) : base(items) { }

    public string Label => Count == 0 ? string.Empty : this[0].GroupLabel;
}
