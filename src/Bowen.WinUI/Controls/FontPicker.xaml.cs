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
    private int _centerSession;

    public FontPicker()
    {
        InitializeComponent();
        PickerFlyout.Opening += (_, _) =>
            FontList.Width = Math.Clamp(PickerButton.ActualWidth, 200, 320);
        PickerFlyout.Opened += OnPickerOpened;
        PickerFlyout.Closed += (_, _) => _centerSession++;
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

    /// <summary>
    /// 打开浮层时把当前选中项滚到视口中间。列表两端时自然退回顶部或底部。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Opened</c> 触发时 ListView 还是 <c>IsLoaded=False</c>：虚拟化容器没有实例化，
    /// 第一次 <c>ScrollIntoView</c> 的请求要到下一轮布局才生效；列表总高度此时也是估值，
    /// 初次滚到目标后还会随实际行高二次校正。只等一次布局、或用
    /// <c>VerticalOffset == target</c> 判断完成都会提前收工，表现就是「没认出当前字体」，
    /// 关掉再开才正常。
    /// </para>
    /// <para>
    /// 所以在打开后的短窗口内轮询：缺容器就重新 <c>ScrollIntoView</c>，有容器就用它
    /// 在视口里的实际位置判断是否完成；列表两端以完整可见为准。
    /// 关闭或重开浮层时 <see cref="_centerSession"/> 变化，旧循环自动退出。
    /// </para>
    /// </remarks>
    private async void OnPickerOpened(object? sender, object e)
    {
        var session = ++_centerSession;
        UpdateSelection();

        for (var attempt = 0; attempt < 20 && session == _centerSession; attempt++)
        {
            if (TryCenterSelectedItem())
            {
                return;
            }

            await Task.Delay(25);
        }
    }

    /// <summary>尝试一次居中；返回 true 表示已完成或无需再试。</summary>
    private bool TryCenterSelectedItem()
    {
        var item = FontList.SelectedItem as FontOption;
        if (item is null)
        {
            UpdateSelection();
            item = FontList.SelectedItem as FontOption;
            if (item is null)
            {
                return true;
            }
        }

        var scrollViewer = FindDescendant<ScrollViewer>(FontList);
        if (scrollViewer is null || scrollViewer.ViewportHeight <= 0)
        {
            return false;
        }

        var container = FontList.ContainerFromItem(item) as ListViewItem;
        if (container is null || container.ActualHeight <= 0)
        {
            // 容器还没实例化，先让选中项进入视口；下一次尝试再取容器。
            FontList.ScrollIntoView(item);
            return false;
        }

        Point position;
        try
        {
            position = container.TransformToVisual(scrollViewer).TransformPoint(new Point(0, 0));
        }
        catch (ArgumentException)
        {
            return false;
        }

        var wantedY = (scrollViewer.ViewportHeight - container.ActualHeight) / 2;
        var maxOffset = Math.Max(0, scrollViewer.ScrollableHeight);
        var target = Math.Clamp(scrollViewer.VerticalOffset + position.Y - wantedY, 0, maxOffset);

        // 用容器在视口里的实际位置判断是否完成，不看偏移相不相等：
        // 首次打开时 ExtentHeight 还是估值，target 可能被错误地夹到 0；
        // 列表两端则本来就滚不到正中，只要容器完整可见就算到位。
        var centered = Math.Abs(position.Y - wantedY) <= 1;
        var visible = position.Y >= -1
                      && position.Y + container.ActualHeight <= scrollViewer.ViewportHeight + 1;
        var atStart = target <= 0 && scrollViewer.VerticalOffset <= 1;
        var atEnd = target >= maxOffset && scrollViewer.VerticalOffset >= maxOffset - 1;

        if (centered || (visible && (atStart || atEnd)))
        {
            return true;
        }

        scrollViewer.ChangeView(null, target, null, disableAnimation: true);
        return false;
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
