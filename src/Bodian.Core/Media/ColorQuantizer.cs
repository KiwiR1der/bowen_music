namespace Bodian.Core.Media;

/// <summary>
/// 从一张位图的像素里挑出主色。
/// </summary>
/// <remarks>
/// <para>
/// <b>用直方图分桶，不用 k-means。</b> k-means 的迭代次数与初始质心不定，同一张图
/// 可能给出不同结果 —— 那没法写稳定的单测。分桶是纯函数、一趟扫描、结果可精确断言。
/// </para>
/// <para>
/// <b>打分而不是取「出现最多的颜色」。</b> 封面里占比最大的往往是白底或黑边，
/// 直接取胜者会拿到一片死白。所以对每个桶按「<b>√像素数</b> × 饱和度 × 亮度适中度」打分。
/// </para>
/// <para>
/// <b>面积要开平方，不能直接乘。</b> 直接乘的话面积大的一定赢 —— 一片白底占 80%、
/// 彩色图案占 20% 的封面，白底照样胜出，那这套打分就等于没写。开平方把面积优势压平，
/// 让饱和度有机会说话，同时仍然尊重「出现得多才算主色」（一个孤立像素依旧赢不了大片色块）。
/// </para>
/// </remarks>
public static class ColorQuantizer
{
    /// <summary>每通道 4 位，共 16³ = 4096 个桶。</summary>
    private const int BucketShift = 4;

    private const int BucketsPerChannel = 1 << (8 - BucketShift);

    private const int BucketCount = BucketsPerChannel * BucketsPerChannel * BucketsPerChannel;

    /// <summary>完全透明的像素不参与统计。</summary>
    private const byte AlphaThreshold = 128;

    /// <summary>
    /// 彩色候选桶的最小面积占比。
    /// </summary>
    /// <remarks>
    /// 白底封面上的彩色主体常常只占几十个像素。没有面积下限时，单个饱和噪点也能靠
    /// 「饱和度满分」胜出；0.5% 在 96×96 上约 46 个像素，足够挡住噪点，又不会漏掉小主体。
    /// </remarks>
    private const double MinimumCandidateFraction = 0.005;

    /// <summary>彩色候选桶的最小像素数；小图上按比例算出来可能只有几个像素。</summary>
    private const int MinimumCandidatePixels = 18;

    /// <summary>低于这个饱和度就不算「彩色候选」。</summary>
    private const double CandidateSaturation = 0.18;

    /// <summary>亮度低于这个值按黑边处理，高于上限按白边处理。</summary>
    private const double CandidateMinimumLuminance = 0.10;

    private const double CandidateMaximumLuminance = 0.92;

    /// <summary>彩色候选里饱和度的权重；比兜底那条高，避免大面积的近灰再次取胜。</summary>
    private const double CandidateSaturationWeight = 0.10;

    /// <summary>没有任何彩色候选时，沿用原来的低饱和度基数，让灰阶封面仍能稳定选出主色。</summary>
    private const double FallbackSaturationWeight = 0.35;

    /// <summary>
    /// 挑出主色。
    /// </summary>
    /// <param name="bgra">
    /// 紧密排列的 BGRA 像素（每像素 4 字节，顺序 B、G、R、A）。
    /// <b>顺序是 BGRA 而不是 RGBA</b> —— Win2D 的 <c>GetPixelColors</c> 与 WIC 都是这个顺序。
    /// </param>
    /// <returns>
    /// 主色。输入为空、或全部像素都透明时返回 <c>null</c>，由调用方决定兜底。
    /// </returns>
    /// <remarks>
    /// <b>先找彩色候选，找不到才退回面积主色。</b> 只按「面积开平方 × 适合度」排序时，
    /// 白底封面上的白色桶经常赢过小面积彩色主体（30% 白底可以压过 0.8% 的棕色），
    /// 主色变成白色后，<see cref="ColorPalette.Ambient"/> 会直接退回默认配色，表现为「没变色」。
    /// 两段式先把近白、近黑和低饱和的桶排除，再在剩下的桶里按面积与饱和度排序。
    /// </remarks>
    public static RgbColor? Dominant(ReadOnlySpan<byte> bgra)
    {
        if (bgra.Length < 4)
        {
            return null;
        }

        var counts = new int[BucketCount];
        var sumR = new int[BucketCount];
        var sumG = new int[BucketCount];
        var sumB = new int[BucketCount];

        var pixels = bgra.Length / 4;
        var visiblePixels = 0;

        for (var i = 0; i < pixels; i++)
        {
            var offset = i * 4;

            if (bgra[offset + 3] < AlphaThreshold)
            {
                continue;
            }

            visiblePixels++;

            byte b = bgra[offset];
            byte g = bgra[offset + 1];
            byte r = bgra[offset + 2];

            var index = ((r >> BucketShift) << (2 * (8 - BucketShift)))
                        | ((g >> BucketShift) << (8 - BucketShift))
                        | (b >> BucketShift);

            counts[index]++;
            sumR[index] += r;
            sumG[index] += g;
            sumB[index] += b;
        }

        var minimumCandidateCount = Math.Max(
            MinimumCandidatePixels,
            (int)Math.Ceiling(visiblePixels * MinimumCandidateFraction));

        var bestCandidateWeight = 0.0;
        RgbColor? bestCandidate = null;

        var bestFallbackWeight = 0.0;
        RgbColor? bestFallback = null;

        for (var index = 0; index < BucketCount; index++)
        {
            var count = counts[index];

            if (count == 0)
            {
                continue;
            }

            var r = (byte)(sumR[index] / count);
            var g = (byte)(sumG[index] / count);
            var b = (byte)(sumB[index] / count);

            var (saturation, luminance) = Chroma(r, g, b);

            var fallbackWeight = Math.Sqrt(count) * Suitability(saturation, luminance, FallbackSaturationWeight);

            if (fallbackWeight > bestFallbackWeight)
            {
                bestFallbackWeight = fallbackWeight;
                bestFallback = new RgbColor(r, g, b);
            }

            if (count < minimumCandidateCount
                || saturation < CandidateSaturation
                || luminance < CandidateMinimumLuminance
                || luminance > CandidateMaximumLuminance)
            {
                continue;
            }

            var candidateWeight = Math.Sqrt(count) * Suitability(saturation, luminance, CandidateSaturationWeight);

            if (candidateWeight <= bestCandidateWeight)
            {
                continue;
            }

            bestCandidateWeight = candidateWeight;
            bestCandidate = new RgbColor(r, g, b);
        }

        return bestCandidate ?? bestFallback;
    }

    /// <summary>饱和度和亮度；亮度用来排除封面的白边与黑边。</summary>
    private static (double Saturation, double Luminance) Chroma(byte r, byte g, byte b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));

        var saturation = max == 0 ? 0.0 : (double)(max - min) / max;

        // 线性化之前先归一化即可 —— 这里只是排序用，不需要严格的光度学精度。
        var luminance = ((0.2126 * r) + (0.7152 * g) + (0.0722 * b)) / 255.0;

        return (saturation, luminance);
    }

    /// <summary>
    /// 这个颜色有多「适合当氛围背景」。
    /// </summary>
    /// <param name="saturation">0..1。</param>
    /// <param name="luminance">0..1。</param>
    /// <param name="saturationWeight">
    /// 饱和度为零时的基础分。彩色候选用低值（0.10），让近灰大色块不容易再赢；
    /// 没有任何彩色候选时用 0.35，保证灰阶封面仍能稳定选出主色。
    /// </param>
    /// <remarks>
    /// 亮度惩罚以 0.55 为中心 —— 那是「中间调」。纯白（1.0）与纯黑（0.0）
    /// 都会被压到三成以下，避免取出封面的白边或黑边。
    /// </remarks>
    private static double Suitability(double saturation, double luminance, double saturationWeight)
        => ((saturationWeight + ((1 - saturationWeight) * saturation))
            * (1 - (0.9 * Math.Abs(luminance - 0.55) / 0.55)));
}
