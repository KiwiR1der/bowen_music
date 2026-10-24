using System.Runtime.CompilerServices;
using Bowen.Core.Media;
using Bowen.Core.Services.Abstractions;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;

namespace Bowen.WinUI.Media;

/// <summary>
/// UI 线程上的有界封面缓存；限制解码尺寸，复用虚拟化容器反复请求的相同图片。
/// </summary>
/// <remarks>
/// <para>
/// <b>内存这一层（L1）背后还有一份磁盘缓存（L2）。</b> 这里命中就直接返回；
/// 没命中时先问磁盘，磁盘有就从本地文件读，没有才走网络并把字节写穿到磁盘。
/// 磁盘是内存的**持久化后盾**，不是替代 —— 进程内的反复请求仍然一次网络都不发。
/// </para>
/// <para>
/// <b><see cref="Get"/> 必须保持同步签名。</b> 它被 <c>Formats.CoverSource</c> 在
/// <c>x:Bind</c> 里同步调用，分布在十几个页面的模板中；改成返回 <c>Task</c> 要动全应用所有模板。
/// 图像解码统一走 <see cref="BitmapImage.SetSourceAsync"/> —— 它允许图片先挂到
/// <c>Image.Source</c>、之后再填充，所以「先给控件、后有图」是安全的。
/// </para>
/// </remarks>
internal static class CoverImageCache
{
    private const int Capacity = 48;
    private const long DecodedByteCapacity = 8L * 1024 * 1024;

    private static long _estimatedDecodedBytes;
    private static readonly SemaphoreSlim DecodeGate = new(2);
    private static ICoverDiskCache? _disk;
    private static readonly Dictionary<(Uri Uri, int Pixels), LinkedListNode<Entry>> Entries = [];
    private static readonly LinkedList<Entry> Recency = new();

    /// <summary>
    /// 每张位图的加载结果。
    /// </summary>
    /// <remarks>
    /// 虚拟化容器回收时会把同一个 <see cref="BitmapImage"/> 再挂到新的 <c>Image</c> 上。
    /// 那时位图早已解码完，控件的 <c>ImageOpened</c> 不会再触发 —— 控件如果只靠那个事件收转圈，
    /// 就会一直转。这里把「已加载」这个事实留在位图旁边，控件可以先查再决定要不要亮圈。
    /// </remarks>
    private static readonly ConditionalWeakTable<BitmapImage, TaskCompletionSource<bool>> States = new();

    /// <summary>接上磁盘层。由 <c>App</c> 在建窗口之前调一次。</summary>
    public static void AttachDiskCache(ICoverDiskCache disk) => _disk = disk;

    /// <summary>丢掉内存这一层。已经赋给 <c>Image.Source</c> 的位图不受影响。</summary>
    public static void Clear()
    {
        Entries.Clear();
        Recency.Clear();
        _estimatedDecodedBytes = 0;
    }

    public static void TrimLargeImages()
    {
        for (var node = Recency.First; node is not null;)
        {
            var next = node.Next;
            if (node.Value.Key.Pixels > 256) Remove(node.Value);
            node = next;
        }
    }

    public static BitmapImage? Get(Uri? uri, int pixels)
    {
        if (uri is null) return null;
        var source = CoverArtUrl.Jpeg(uri, pixels)!;
        var key = (source, pixels);
        if (Entries.TryGetValue(key, out var cached))
        {
            Recency.Remove(cached);
            Recency.AddFirst(cached);
            return cached.Value.Image;
        }

        // 先指定解码尺寸，再开始请求，磁盘中的原样字节也遵循同一约定。
        var image = new BitmapImage { DecodePixelWidth = pixels };
        var fallback = CoverArtUrl.Fallback(source) ?? (source != uri ? uri : null);
        var entry = new Entry(key, image, (long)pixels * pixels * 4, fallback);
        States.AddOrUpdate(image, new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));

        // 先登记再发起加载：失败事件可能很快到达，必须能移除对应条目。
        var node = Recency.AddFirst(entry);
        Entries.Add(key, node);
        _estimatedDecodedBytes += entry.EstimatedBytes;
        while ((Entries.Count > Capacity || _estimatedDecodedBytes > DecodedByteCapacity) && Recency.Last is { } oldest)
        {
            Remove(oldest.Value);
        }

        Load(entry, source);
        return image;
    }

    private static void Load(Entry entry, Uri source) => _ = LoadAsync(entry, source);

    private static async Task LoadAsync(Entry entry, Uri source)
    {
        try
        {
            await LoadStreamAsync(entry, source);
            Settle(entry.Image, true);
        }
        catch (Exception)
        {
            if (!entry.HasTriedFallback && entry.Fallback is { } fallback)
            {
                entry.HasTriedFallback = true;
                await LoadAsync(entry, fallback);
                return;
            }

            Remove(entry);
            Settle(entry.Image, false);
        }
    }

    private static void Settle(BitmapImage image, bool loaded)
    {
        if (States.TryGetValue(image, out var state))
        {
            state.TrySetResult(loaded);
        }
    }

    /// <summary>位图是否已经有结果；<paramref name="loaded"/> 为 <c>false</c> 表示加载失败。</summary>
    internal static bool TryGetState(BitmapImage image, out bool loaded)
    {
        if (States.TryGetValue(image, out var state) && state.Task.IsCompleted)
        {
            loaded = state.Task.Result;
            return true;
        }

        loaded = false;
        return false;
    }

    /// <summary>还没出结果时返回可等待的任务；不是本缓存创建的位图返回 <c>null</c>。</summary>
    internal static Task<bool>? TryGetSettlement(BitmapImage image) =>
        States.TryGetValue(image, out var state) ? state.Task : null;

    private static async Task LoadStreamAsync(Entry entry, Uri source)
    {
        var cacheKey = CoverCacheKey.For(source, entry.Key.Pixels);
        if (_disk is not null)
        {
            // 网络图也先进入已有的有界磁盘缓存，复用同一下载，避免 URI 图片缓存再持有一份解码资源。
            if (!_disk.TryGetPath(cacheKey, out _)) await _disk.WriteThroughAsync(cacheKey, source);
            if (_disk.TryGetPath(cacheKey, out var path) && path is not null)
            {
                try
                {
                    using var file = File.OpenRead(path);
                    await ApplyStreamAsync(entry.Image, file.AsRandomAccessStream());
                    return;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // 已缓存的文件被清理或读取失败，仍可退回原地址；不删除用户磁盘上的文件。
                }
            }
        }
        using var stream = await RandomAccessStreamReference.CreateFromUri(source).OpenReadAsync();
        await ApplyStreamAsync(entry.Image, stream);
    }

    private static async Task ApplyStreamAsync(BitmapImage image, IRandomAccessStream stream)
    {
        await DecodeGate.WaitAsync();
        var failed = false;
        void OnFailed(object sender, ExceptionRoutedEventArgs args) => failed = true;
        image.ImageFailed += OnFailed;
        try
        {
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var scale = Math.Min(1, image.DecodePixelWidth / (double)Math.Max(decoder.PixelWidth, decoder.PixelHeight));
            if (scale >= 1)
            {
                stream.Seek(0);
                await image.SetSourceAsync(stream);
            }
            else
            {
                // DecodePixelWidth 不限制 WinUI 持有的原始编码数据。先生成真实缩略图，
                // 让未知 CDN 返回的 3000/4000 px 图片也受本地缓存预算约束。
                var width = Math.Max(1U, (uint)Math.Round(decoder.PixelWidth * scale));
                var height = Math.Max(1U, (uint)Math.Round(decoder.PixelHeight * scale));
                using var pixels = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    new BitmapTransform { ScaledWidth = width, ScaledHeight = height },
                    ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
                using var thumbnail = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, thumbnail);
                encoder.SetSoftwareBitmap(pixels);
                await encoder.FlushAsync();
                thumbnail.Seek(0);
                await image.SetSourceAsync(thumbnail);
            }
            if (failed) throw new InvalidDataException("Cover image decoding failed.");
        }
        finally
        {
            image.ImageFailed -= OnFailed;
            DecodeGate.Release();
        }
    }

    private static void Remove(Entry entry)
    {
        // 旧位图可能已被淘汰或清空；迟到的失败不能移除同地址的新位图。
        if (!Entries.TryGetValue(entry.Key, out var node) || !ReferenceEquals(node.Value, entry)) return;

        Entries.Remove(entry.Key);
        Recency.Remove(node);
        _estimatedDecodedBytes -= entry.EstimatedBytes;
    }

    private sealed record Entry((Uri Uri, int Pixels) Key, BitmapImage Image, long EstimatedBytes, Uri? Fallback)
    {
        public bool HasTriedFallback { get; set; }
    }
}
