using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Bodian.WinUI.Services;

/// <summary>页面离屏前记住它滚到哪，回到这一页时放回原处。</summary>
/// <remarks>
/// <para>
/// 换页是宿主把 <c>ContentControl.Content</c> 从旧页换成新页（见 <see cref="NavigationService"/>）：
/// 页面实例留着，但滚动位置属于列表控件的模板内部状态，重新挂上树时从 0 开始。
/// 表现是「返回后停在最顶」，还连着更隐蔽的一处 ——
/// 详情返回的共享封面落点是原来那张卡片，卡片不在视口里时整段飞行会被静默跳过。
/// </para>
/// <para>
/// <b>只记像素偏移，不碰数据源。</b> 早先那套离屏资源释放顺带清空了列表的 <c>ItemsSource</c>，
/// 造成歌单与专辑曲目显示为空的回归（记录见 <c>docs/performance.md</c>）。
/// 滚动位置是纯呈现状态，恢复它不必重建列表，也不调用 <c>ScrollIntoView</c>，
/// 所以不会动到用户没滚过的那部分内容。
/// </para>
/// <para>
/// <b>落位之前这些容器不显示（<see cref="AppMotion.Hide"/>）。</b> 否则列表会先按「顶部」画一帧、
/// 再跳到原位置 —— 位置对上了，那一下「闪」也留下了。先把布局赶出来让它落在首帧上，
/// 万一没赶上就在 <see cref="RevealWindow"/> 内继续试，超时按当前落点显示。
/// </para>
/// </remarks>
internal static class ScrollMemory
{
    /// <summary>落位判定的容差；差不到一个像素就算到了。</summary>
    private const double Tolerance = 0.5;

    /// <summary>
    /// 遮挡窗口：这么久还没落位就先显示出来，剩下的偏移不再追。
    /// </summary>
    /// <remarks>
    /// 追得久了会变成另一种毛病 —— 列表已经显示出来，它还在几帧里继续挪位置，
    /// 那是用户正盯着的时候在动。宁可停在差一点的地方。
    /// </remarks>
    private static readonly TimeSpan RevealWindow = TimeSpan.FromMilliseconds(250);

    private static readonly bool Diagnostics = Environment.GetEnvironmentVariable("BODIAN_LYRICS_DIAGNOSTICS") == "1";
    private static readonly ConditionalWeakTable<Page, State> States = new();
    private static readonly ILogger Logger = (Application.Current.Resources["BodianLoggerFactory"] as ILoggerFactory
        ?? NullLoggerFactory.Instance).CreateLogger("ScrollMemory");

    /// <summary>页面即将离屏：记下它各处滚到哪。</summary>
    public static void Capture(Page? page)
    {
        // 歌词页与 MV 页换的是沉浸宿主，常规页留在外壳里当背景，位置本来就没丢。
        if (page is null or Views.LyricsPage or Views.MvPage) return;
        var state = States.GetValue(page, static _ => new State());
        state.Version++;
        state.Entries.Clear();
        Collect(page, state.Entries);
        if (Diagnostics && state.Entries.Count > 0)
            Logger.LogInformation("滚动位置记录：{Page}，{Count} 处", page.GetType().Name, state.Entries.Count);
    }

    /// <summary>页面挂回来之后把偏移放回去；没有记录时立刻结束。</summary>
    public static Task RestoreAsync(Page page)
    {
        if (!States.TryGetValue(page, out var state) || state.Entries.Count == 0) return Task.CompletedTask;
        // entries 是「遮挡过、必须放出来」的那一份，全程不动；pending 是「还没落位」的那一份，落位即去掉。
        var entries = state.Entries.ToList();
        state.Entries.Clear();
        var pending = entries.ToList();
        var version = ++state.Version;
        var completion = new TaskCompletionSource();
        var timer = page.DispatcherQueue.CreateTimer();
        timer.Interval = RevealWindow;
        timer.IsRepeating = false;
        var measured = false;
        EventHandler<object>? layout = null;
        RoutedEventHandler? loaded = null;

        // 落位前先遮住：列表会先按「顶部」画一帧、再跳到原位置，那一下就是看得见的「闪」。
        foreach (var entry in entries) AppMotion.Hide(entry.Host);

        void Finish()
        {
            timer.Stop();
            timer.Tick -= OnTimeout;
            page.LayoutUpdated -= layout;
            page.Loaded -= loaded;
            // 落位的、没落位的都要放出来 —— 遮挡过的东西必须在这一条路径上还原。
            foreach (var entry in entries) _ = AppMotion.RevealAsync(entry.Host);
            if (Diagnostics)
                Logger.LogInformation("滚动位置恢复：{Page}，剩 {Pending} 处未落位", page.GetType().Name, pending.Count);
            completion.TrySetResult();
        }

        void Check()
        {
            // 期间又发生了一次导航：这一页已经不是当前页，记下的位置留到下次回来再用。
            if (version != state.Version)
            {
                Finish();
                return;
            }
            if (!page.IsLoaded) return;
            if (!measured)
            {
                // <c>Loaded</c> 早于首次 arrange，这一刻列表还没有可滚范围，偏移落不下去，
                // 只能顺延到下一轮布局 —— 那几帧正是「闪」的来源。先把布局赶出来，定位就能落在首帧上。
                measured = true;
                page.UpdateLayout();
            }
            for (var index = pending.Count - 1; index >= 0; index--)
                if (Place(pending[index])) pending.RemoveAt(index);
            if (pending.Count == 0) Finish();
        }

        void OnTimeout(DispatcherQueueTimer sender, object args) => Finish();
        layout = (_, _) => Check();
        loaded = (_, _) => Check();
        page.LayoutUpdated += layout;
        page.Loaded += loaded;
        timer.Tick += OnTimeout;
        timer.Start();
        Check();
        return completion.Task;
    }

    /// <summary>把一处偏移放回去；已经到位或这一处已经不在这页上时返回 <c>true</c>。</summary>
    /// <remarks>只在页面 <c>Loaded</c> 之后调用：那一刻还在树上、但 <c>XamlRoot</c> 为空的容器，是真的换了内容。</remarks>
    private static bool Place(Entry entry)
    {
        if (entry.Host.XamlRoot is null) return true;
        // 容器的模板还没应用（列表刚挂上树），下一帧再来一次。
        if (ScrollOf(entry.Host) is not { } scroll) return false;
        if (Near(scroll.HorizontalOffset, entry.Horizontal) && Near(scroll.VerticalOffset, entry.Vertical)) return true;
        scroll.ChangeView(entry.Horizontal, entry.Vertical, null, true);
        return false;
    }

    private static void Collect(DependencyObject root, List<Entry> entries)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            // 列表自带滚动：模板里的 ScrollViewer 会随重新挂树重建，所以记的键是列表这个稳定实例。
            // 列表内部不再往下走 —— 行模板与条目会被虚拟化回收，记下来的位置会张冠李戴。
            if (child is ListViewBase list)
            {
                if (ScrollOf(list) is { } inner) Record(list, inner, entries);
                continue;
            }
            if (child is ScrollViewer viewer)
            {
                Record(viewer, viewer, entries);
                continue;
            }
            Collect(child, entries);
        }
    }

    private static void Record(FrameworkElement host, ScrollViewer scroll, List<Entry> entries)
    {
        // 没滚动过的容器不必记：绝大多数列表离屏时就停在顶上。
        if (Near(scroll.HorizontalOffset, 0) && Near(scroll.VerticalOffset, 0)) return;
        entries.Add(new Entry(host, scroll.HorizontalOffset, scroll.VerticalOffset));
    }

    /// <summary>容器自己的滚动视图；列表没有它，独立 <see cref="ScrollViewer"/> 就是它自己。</summary>
    private static ScrollViewer? ScrollOf(DependencyObject element)
    {
        if (element is ScrollViewer viewer) return viewer;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            if (ScrollOf(VisualTreeHelper.GetChild(element, index)) is { } found) return found;
        return null;
    }

    private static bool Near(double left, double right) => Math.Abs(left - right) <= Tolerance;

    private sealed class State
    {
        public int Version;
        public List<Entry> Entries { get; } = [];
    }

    /// <summary>一处待恢复的位置：<paramref name="Host"/> 是重新挂树后仍然可用的那个元素。</summary>
    private sealed record Entry(FrameworkElement Host, double Horizontal, double Vertical);
}
