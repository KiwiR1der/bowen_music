using System.Collections.ObjectModel;
using Bowen.WinUI.Playback;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;

namespace Bowen.WinUI.ViewModels;

/// <summary>
/// 右侧播放队列抽屉的状态与动作。**单例**，与主窗口同寿命。
/// </summary>
/// <remarks>
/// <b>条目有增删挪动就整表重建，不做增量同步。</b> 那类变动不频繁（换页、增删、拖动重排），
/// 而增量同步要自己维护一套「哪一行对应哪个下标」的影子状态 ——
/// 队列那边刚因为下标重映射踩过一遍，这里没必要再来一次。
/// <para>
/// <b>例外是「条目没动」的变更</b>：点行切歌、一首放完自动续播只挪游标，切播放模式只改
/// 「能不能拖」。这时就地更新行上的 <c>IsCurrent</c> / <c>CanReorder</c>，不重建集合 ——
/// 整表重建会让 ListView 的滚动位置弹回顶部，用户看到的是「没动列表却刷了一下」。
/// </para>
/// </remarks>
public sealed partial class PlayQueueViewModel : ObservableObject
{
    private readonly PlaybackCoordinator _coordinator;

    /// <summary>构造时所在的线程就是界面线程：这个 VM 由 DI 在装配主窗口时创建。</summary>
    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();

    public PlayQueueViewModel(PlaybackCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);

        _coordinator = coordinator;
        _coordinator.Queue.Changed += (_, _) => RefreshIfOpen();
    }

    public ObservableCollection<PlayQueueRow> Rows { get; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; set; } = true;

    [ObservableProperty]
    public partial string CountText { get; set; } = "";

    /// <summary>
    /// 抽屉是不是开着。由界面（主窗口）置位，抽屉的显隐直接绑它。
    /// </summary>
    /// <remarks>
    /// <b>开着的时候才重建列表</b>：队列每次切歌都会触发 <c>Changed</c>，
    /// 而重建会清空再填满 <c>ObservableCollection</c>、让 <c>ListView</c> 整个重新测量 ——
    /// 抽屉没拉开就没人看，这份力气白花。
    /// </remarks>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    partial void OnIsOpenChanged(bool value)
    {
        // 打开时补一次：关着的那段时间队列可能已经变过好几轮了。
        if (value)
        {
            Refresh();
        }
    }

    /// <summary>
    /// 小窗那条播放队列面板是不是开着。
    /// </summary>
    /// <remarks>
    /// <b>与 <see cref="IsOpen"/> 分开是必须的，不是一个布尔抄了两遍。</b>
    /// <see cref="IsOpen"/> 被主窗口的抽屉独占（抽屉的显隐直接绑它），而小窗与主窗口
    /// 可能同时开着：小窗的面板去改 <see cref="IsOpen"/> 会把主窗口的抽屉一起拉开；
    /// 反过来不改，<see cref="RefreshIfOpen"/> 会早早返回，小窗面板里显示的是陈旧队列。
    /// 两个开关共用同一份 <see cref="Rows"/>，所以两处看到的队列仍然一致。
    /// </remarks>
    [ObservableProperty]
    public partial bool IsMiniPlayerOpen { get; set; }

    partial void OnIsMiniPlayerOpenChanged(bool value)
    {
        // 理由同 OnIsOpenChanged：打开时补一次。
        if (value)
        {
            Refresh();
        }
    }

    /// <summary>跳到第 <paramref name="position"/> 首并开始播。</summary>
    public void Play(int position) => _ = _coordinator.PlayQueueItemAsync(position);

    /// <summary>从队列里删掉第 <paramref name="position"/> 首。</summary>
    public Task RemoveAsync(int position) => _coordinator.RemoveQueueItemAsync(position);

    /// <summary>删除入口的同步包装；界面要等退场动画时直接调 <see cref="RemoveAsync"/>。</summary>
    public void Remove(int position) => _ = RemoveAsync(position);

    /// <summary>清空队列。确认框由界面负责，这里只管执行。</summary>
    public void Clear() => _coordinator.ClearQueue();

    /// <summary>
    /// 把第 <paramref name="from"/> 首挪到第 <paramref name="to"/> 位（挪完后的最终下标）。
    /// </summary>
    /// <remarks>
    /// 行号与行对象靠 <c>Changed → RefreshIfOpen</c> 自动重建，不用在这里同步维护 ——
    /// 那次重建排在 dispatcher 下一轮，正好落在触发它的那个指针事件处理之后。
    /// </remarks>
    public void Move(int from, int to) => _ = _coordinator.MoveQueueItem(from, to);

    private void RefreshIfOpen()
    {
        // 两个宿主任意一个开着就要重建 —— 见 IsMiniPlayerOpen 的说明。
        if (!IsOpen && !IsMiniPlayerOpen)
        {
            return;
        }

        // 队列很可能就是在面板自己的事件处理里被改的（点行切歌、点行尾的删除）。
        // 同步重建等于在 ItemsControl 处理事件的过程中把它脚下的集合抽掉，所以排到下一轮再重建。
        if (_dispatcher is null || !_dispatcher.TryEnqueue(Refresh))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var queue = _coordinator.Queue;
        var current = queue.CurrentIndex;
        var canReorder = _coordinator.CanReorderQueue;

        // 只删了一行时也只删对应的行，保留其余容器与滚动位置。
        if (TryApplyRemoval(queue, current, canReorder))
        {
            return;
        }

        // 队列条目没动时只改行上的状态，不重建。
        // ★ 不能随手整表重建：集合一清空，ListView 的滚动位置就弹回顶部 ——
        //   点行切歌、一首放完自动续播、以及只切播放模式都会走到这里。
        //   切模式只影响 CanReorder（拖动条显隐），它现在是可通知属性，就地改就行。
        if (CanReuseRows(queue))
        {
            for (var i = 0; i < Rows.Count; i++)
            {
                Rows[i].IsCurrent = i == current;
                Rows[i].CanReorder = canReorder;
            }

            return;
        }

        Rows.Clear();

        for (var i = 0; i < queue.Count; i++)
        {
            Rows.Add(new PlayQueueRow
            {
                Source = queue.Items[i],
                Position = i,
                IsCurrent = i == current,
                CanReorder = canReorder,
            });
        }

        IsEmpty = Rows.Count == 0;
        CountText = Rows.Count == 0 ? "" : $"共 {Rows.Count} 首";
    }

    /// <summary>
    /// 队列只少了一首时，就地删掉对应那一行并更新后面的行号。
    /// </summary>
    /// <remarks>
    /// <b>不整表重建的原因与切歌相同：</b> 清空集合会让 ListView 的滚动位置弹回顶部，
    /// 用户删中间某首时会被直接甩回队首。这里先确认除这一行外其余引用都能按位移对上；
    /// 对不上（同时发生重排、清空等）才让 <see cref="Refresh"/> 走整表重建。
    /// </remarks>
    private bool TryApplyRemoval(PlayQueue queue, int current, bool canReorder)
    {
        if (Rows.Count != queue.Count + 1)
        {
            return false;
        }

        var removed = -1;

        for (var i = 0; i < queue.Count; i++)
        {
            if (!ReferenceEquals(Rows[i].Source, queue.Items[i]))
            {
                removed = i;
                break;
            }
        }

        if (removed < 0)
        {
            removed = Rows.Count - 1;
        }

        for (var i = removed; i < queue.Count; i++)
        {
            if (!ReferenceEquals(Rows[i + 1].Source, queue.Items[i]))
            {
                return false;
            }
        }

        Rows.RemoveAt(removed);

        for (var i = 0; i < Rows.Count; i++)
        {
            Rows[i].Position = i;
            Rows[i].IsCurrent = i == current;
            Rows[i].CanReorder = canReorder;
        }

        IsEmpty = Rows.Count == 0;
        CountText = Rows.Count == 0 ? "" : $"共 {Rows.Count} 首";
        return true;
    }

    /// <summary>
    /// 现有这些行还能不能接着用，不必重建。
    /// </summary>
    /// <remarks>
    /// <b>按下标逐个比引用。</b> 增删、拖动重排会让某一格对不上，于是整表重建；
    /// 只动游标或只切播放模式的变更则每一格都对得上 —— 行号
    /// （<see cref="PlayQueueRow.Position"/>）没变，<c>CanReorder</c> 就地更新。
    /// </remarks>
    private bool CanReuseRows(PlayQueue queue)
    {
        if (Rows.Count != queue.Count)
        {
            return false;
        }

        for (var i = 0; i < Rows.Count; i++)
        {
            if (!ReferenceEquals(Rows[i].Source, queue.Items[i]))
            {
                return false;
            }
        }

        return true;
    }
}
