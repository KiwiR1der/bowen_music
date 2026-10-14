using System.Net;
using System.Security.Cryptography;
using Bodian.Core.Models;
using Bodian.Core.Services.Implementations;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 下载安装包。**零真实网络** —— 处理器是桩，落盘走临时目录。
/// </summary>
public sealed class AppUpdateDownloadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>比复制缓冲（80 KB）大，保证循环至少跑两轮。</summary>
    private static readonly byte[] Payload = MakePayload(200 * 1024);

    private static string NewDirectory() => Path.Combine(
        Path.GetTempPath(), "bodian-update-download-tests", Guid.NewGuid().ToString("N"));

    private static byte[] MakePayload(int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)(i % 251);
        }

        return bytes;
    }

    private static string Sha256Of(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static AppUpdateAsset Asset(string sha = "", long size = 0, string name = "BowenMusic_0.2.0_x64_setup.exe") =>
        new(name, size, new Uri("https://example.invalid/" + name), sha);

    // ── 正常路径 ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Download_WritesTheFileAtTheExpectedPath()
    {
        var directory = NewDirectory();
        using var service = new GitHubReleaseUpdateService(new DownloadStub(Payload), Configured(), downloadDirectory: directory);

        var result = await service.DownloadAsync(Asset(), cancellationToken: Ct);

        Assert.Equal(AppUpdateDownloadStatus.Completed, result.Status);
        Assert.Equal(Path.Combine(directory, "BowenMusic_0.2.0_x64_setup.exe"), result.FilePath);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(result.FilePath!, Ct));

        // 中间文件必须已经改名，不能留下一个 .part 让用户以为下了两份。
        Assert.False(File.Exists(result.FilePath + ".part"));
    }

    [Fact]
    public async Task Download_ReportsProgressUpToTheTotal()
    {
        var progress = new RecordingProgress<AppUpdateProgress>();
        using var service = new GitHubReleaseUpdateService(new DownloadStub(Payload), Configured(), downloadDirectory: NewDirectory());

        var result = await service.DownloadAsync(Asset(size: Payload.Length), progress, Ct);

        Assert.Equal(AppUpdateDownloadStatus.Completed, result.Status);
        Assert.NotEmpty(progress.Reports);

        var last = progress.Reports[^1];
        Assert.Equal(Payload.Length, last.ReceivedBytes);
        Assert.Equal(Payload.Length, last.TotalBytes);
        Assert.Equal(1d, last.Fraction);
    }

    [Fact]
    public async Task Download_WithoutAKnownLength_ReportsNoFraction()
    {
        var progress = new RecordingProgress<AppUpdateProgress>();
        using var service = new GitHubReleaseUpdateService(
            new DownloadStub(Payload, declareLength: false), Configured(), downloadDirectory: NewDirectory());

        var result = await service.DownloadAsync(Asset(), progress, Ct);

        Assert.Equal(AppUpdateDownloadStatus.Completed, result.Status);

        // 两边都没给总长时如实报 null：界面用不确定进度条，不要编一个数字出来。
        Assert.Null(progress.Reports[^1].TotalBytes);
        Assert.Null(progress.Reports[^1].Fraction);
    }

    // ── 校验 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Download_VerifiesTheChecksum()
    {
        var directory = NewDirectory();
        using var service = new GitHubReleaseUpdateService(new DownloadStub(Payload), Configured(), downloadDirectory: directory);

        var result = await service.DownloadAsync(Asset(Sha256Of(Payload)), cancellationToken: Ct);

        Assert.Equal(AppUpdateDownloadStatus.Completed, result.Status);
    }

    [Fact]
    public async Task Download_OnChecksumMismatch_DiscardsTheFile()
    {
        var directory = NewDirectory();
        using var service = new GitHubReleaseUpdateService(new DownloadStub(Payload), Configured(), downloadDirectory: directory);

        var result = await service.DownloadAsync(Asset(new string('f', 64)), cancellationToken: Ct);

        Assert.Equal(AppUpdateDownloadStatus.Failed, result.Status);

        // 半截/被改过的包必须消失：留下一个像模像样的坏安装包比失败更糟。
        Assert.Empty(Directory.GetFiles(directory));
    }

    // ── 取消与失败 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Download_WhenCancelled_LeavesNothingBehind()
    {
        var directory = NewDirectory();
        using var cts = new CancellationTokenSource();
        var progress = new CancelOnFirstReport(cts);
        using var service = new GitHubReleaseUpdateService(new DownloadStub(Payload), Configured(), downloadDirectory: directory);

        var result = await service.DownloadAsync(Asset(), progress, cts.Token);

        // 取消不是错误 —— 界面不该为它弹一条报错。
        Assert.Equal(AppUpdateDownloadStatus.Cancelled, result.Status);
        Assert.Null(result.FilePath);
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task Download_OnHttpError_FailsWithoutWritingAnything()
    {
        var directory = NewDirectory();
        using var service = new GitHubReleaseUpdateService(
            new DownloadStub(Payload, status: HttpStatusCode.NotFound), Configured(), downloadDirectory: directory);

        var result = await service.DownloadAsync(Asset(), cancellationToken: Ct);

        Assert.Equal(AppUpdateDownloadStatus.Failed, result.Status);
        Assert.DoesNotContain("Exception", result.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task Download_OnNetworkFailure_ReportsAFriendlyFailure()
    {
        var directory = NewDirectory();
        using var service = new GitHubReleaseUpdateService(
            new ThrowingHandler(new HttpRequestException("断网了")), Configured(), downloadDirectory: directory);

        var result = await service.DownloadAsync(Asset(), cancellationToken: Ct);

        Assert.Equal(AppUpdateDownloadStatus.Failed, result.Status);
        Assert.DoesNotContain("Exception", result.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(directory));
    }

    // ── 文件名 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Download_KeepsTheFileInsideTheDownloadDirectory()
    {
        // 文件名来自外部接口。一个带路径分隔符的资产名不该能写到目录外面去。
        var directory = NewDirectory();
        using var service = new GitHubReleaseUpdateService(new DownloadStub(Payload), Configured(), downloadDirectory: directory);

        var result = await service.DownloadAsync(Asset(name: @"..\..\evil.exe"), cancellationToken: Ct);

        Assert.Equal(AppUpdateDownloadStatus.Completed, result.Status);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(directory, "evil.exe")),
            Path.GetFullPath(result.FilePath!));
    }

    [Fact]
    public async Task Download_WithNoUsableName_FailsInsteadOfGuessing()
    {
        using var service = new GitHubReleaseUpdateService(
            new DownloadStub(Payload), Configured(), downloadDirectory: NewDirectory());

        var result = await service.DownloadAsync(Asset(name: "   "), cancellationToken: Ct);

        Assert.Equal(AppUpdateDownloadStatus.Failed, result.Status);
    }

    private static AppUpdateOptions Configured() => new()
    {
        Owner = "someone",
        Repository = "bodian",
        ProjectUrl = "https://github.com/someone/bodian",
    };

    /// <summary>取消第一次上报之后的下载。</summary>
    /// <remarks>
    /// 不用 <c>Progress&lt;T&gt;</c>：它把回调投递到同步上下文，测试里没有上下文就退化成线程池排队，
    /// 什么时候跑到就不确定了。见 <see cref="RecordingProgress{T}"/> 的说明。
    /// </remarks>
    private sealed class CancelOnFirstReport(CancellationTokenSource source) : IProgress<AppUpdateProgress>
    {
        public void Report(AppUpdateProgress value) => source.Cancel();
    }

    /// <summary>按需返回一段字节的处理器。</summary>
    private sealed class DownloadStub : HttpMessageHandler
    {
        private readonly byte[] _payload;
        private readonly bool _declareLength;
        private readonly HttpStatusCode _status;

        public DownloadStub(byte[] payload, bool declareLength = true, HttpStatusCode status = HttpStatusCode.OK)
        {
            _payload = payload;
            _declareLength = declareLength;
            _status = status;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_status != HttpStatusCode.OK)
            {
                return Task.FromResult(new HttpResponseMessage(_status));
            }

            var content = new StreamContent(
                _declareLength ? new MemoryStream(_payload) : new NonSeekableStream(_payload));

            if (_declareLength)
            {
                content.Headers.ContentLength = _payload.Length;
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => throw exception;
    }

    /// <summary>不可寻址的流：<c>StreamContent</c> 因此算不出长度，响应里就不会有 <c>Content-Length</c>。</summary>
    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override void Flush() => _inner.Flush();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
