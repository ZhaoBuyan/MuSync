using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

public class UpdateCheckerTests
{
    private static UpdateChecker.UpdateAsset Asset(string name, long size = 100) =>
        new() { Name = name, Size = size, DownloadUrl = "https://example.com/" + name };

    [Fact]
    public void SelectAsset_FullVersion_PicksFullPackage()
    {
        var assets = new List<UpdateChecker.UpdateAsset> { Asset("MuSync.exe"), Asset("MuSync-lite.exe") };
        var selected = UpdateChecker.SelectAsset(assets, "MuSync.exe");
        Assert.NotNull(selected);
        Assert.Equal("MuSync.exe", selected!.Name);
    }

    [Fact]
    public void SelectAsset_LiteVersion_PicksLitePackage()
    {
        var assets = new List<UpdateChecker.UpdateAsset> { Asset("MuSync.exe"), Asset("MuSync-lite.exe") };
        var selected = UpdateChecker.SelectAsset(assets, "MuSync-lite.exe");
        Assert.NotNull(selected);
        Assert.Equal("MuSync-lite.exe", selected!.Name);
    }

    [Fact]
    public void SelectAsset_RenamedExe_FallsBackToFullPackage()
    {
        var assets = new List<UpdateChecker.UpdateAsset> { Asset("MuSync-lite.exe"), Asset("MuSync.exe") };
        var selected = UpdateChecker.SelectAsset(assets, "我的音乐.exe");
        Assert.NotNull(selected);
        Assert.Equal("MuSync.exe", selected!.Name);
    }

    [Fact]
    public void SelectAsset_SinglePackage_Works()
    {
        var assets = new List<UpdateChecker.UpdateAsset> { Asset("MuSync-lite.exe") };
        var selected = UpdateChecker.SelectAsset(assets, "MuSync.exe");
        Assert.NotNull(selected);
        Assert.Equal("MuSync-lite.exe", selected!.Name);
    }

    [Fact]
    public void SelectAsset_NoExeAssets_ReturnsNull()
    {
        var assets = new List<UpdateChecker.UpdateAsset> { Asset("MuSync.zip"), Asset("source.tar.gz") };
        Assert.Null(UpdateChecker.SelectAsset(assets, "MuSync.exe"));
    }

    [Fact]
    public void SelectAsset_EmptyList_ReturnsNull()
    {
        var assets = new List<UpdateChecker.UpdateAsset>();
        Assert.Null(UpdateChecker.SelectAsset(assets, "MuSync.exe"));
    }
}

public class UpdateDownloaderTests
{
    [Theory]
    [InlineData("MuSync-v0.2.1.exe", "0.2.1")]
    [InlineData("MuSync-lite-v0.2.1.exe", "0.2.1")]
    [InlineData("MuSync-v0.2.10.exe", "0.2.10")]
    [InlineData("MuSync-v1.0.0.exe", "1.0.0")]
    public void TryParsePackageVersion_ValidNames(string fileName, string expected)
    {
        Assert.True(UpdateDownloader.TryParsePackageVersion(fileName, out var version));
        Assert.Equal(expected, version.ToString(3));
    }

    [Theory]
    [InlineData("MuSync.exe")]
    [InlineData("MuSync-lite.exe")]
    [InlineData("readme.txt")]
    [InlineData("MuSync-v0.2.1.exe.part")]
    [InlineData("MuSync-vabc.exe")]
    [InlineData("MuSync-v0.2.1")]
    public void TryParsePackageVersion_InvalidNames(string fileName)
    {
        Assert.False(UpdateDownloader.TryParsePackageVersion(fileName, out _));
    }

    [Fact]
    public void GetPackagePath_UsesVersionedName()
    {
        var asset = new UpdateChecker.UpdateAsset
        {
            Name = "MuSync-lite.exe",
            Size = 1,
            DownloadUrl = "https://example.com"
        };
        var path = UpdateDownloader.GetPackagePath(asset, "0.2.1");
        Assert.Equal("MuSync-lite-v0.2.1.exe", Path.GetFileName(path));
    }

    [Fact]
    public void CleanupOldPackages_DeletesOnlyOlderPackages()
    {
        var dir = Directory.CreateTempSubdirectory("musync-update-test-").FullName;
        try
        {
            var keep = Path.Combine(dir, "MuSync-v0.2.1.exe");
            var oldFull = Path.Combine(dir, "MuSync-v0.2.0.exe");
            var oldLite = Path.Combine(dir, "MuSync-lite-v0.2.0.exe");
            var sameVersionOtherVariant = Path.Combine(dir, "MuSync-lite-v0.2.1.exe");
            var unrelatedExe = Path.Combine(dir, "MuSync.exe");
            var notExe = Path.Combine(dir, "notes.txt");
            File.WriteAllText(keep, "new");
            File.WriteAllText(oldFull, "old");
            File.WriteAllText(oldLite, "old");
            File.WriteAllText(sameVersionOtherVariant, "same");
            File.WriteAllText(unrelatedExe, "user-file");
            File.WriteAllText(notExe, "text");

            UpdateDownloader.CleanupOldPackages(dir, keep, new Version(0, 2, 1));

            Assert.True(File.Exists(keep));
            Assert.False(File.Exists(oldFull));
            Assert.False(File.Exists(oldLite));
            Assert.True(File.Exists(sameVersionOtherVariant));
            Assert.True(File.Exists(unrelatedExe));
            Assert.True(File.Exists(notExe));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CleanupOldPackages_MissingDirectory_DoesNotThrow()
    {
        var dir = Path.Combine(Path.GetTempPath(), "musync-missing-" + Guid.NewGuid().ToString("N"));
        UpdateDownloader.CleanupOldPackages(dir, Path.Combine(dir, "MuSync-v0.2.1.exe"), new Version(0, 2, 1));
        Assert.False(Directory.Exists(dir));
    }
}

/// <summary>
/// 下载流程端到端测试：用回环 TCP 手写 HTTP 服务器，验证
/// 「成功转正 / 校验失败 / HTTP 错误 / 用户取消」四条路径都不会留下残余文件。
/// </summary>
public class UpdateDownloaderIntegrationTests
{
    private sealed class InlineProgress(Action<UpdateDownloader.DownloadProgress> onReport)
        : IProgress<UpdateDownloader.DownloadProgress>
    {
        public void Report(UpdateDownloader.DownloadProgress value) => onReport(value);
    }

    /// <summary>极简单次请求 HTTP 服务器（避免 HttpListener 的 URL ACL 权限要求）。</summary>
    private sealed class OneShotServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _serveTask;

        public string Url { get; }

        public OneShotServer(byte[] body, int statusCode = 200, int chunkDelayMs = 0)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Url = $"http://127.0.0.1:{port}/file.exe";
            _serveTask = Task.Run(() => ServeAsync(body, statusCode, chunkDelayMs));
        }

        private async Task ServeAsync(byte[] body, int statusCode, int chunkDelayMs)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                await ReadRequestHeadersAsync(stream);
                var header =
                    $"HTTP/1.1 {statusCode} X\r\nContent-Length: {body.Length}\r\n" +
                    "Content-Type: application/octet-stream\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
                if (chunkDelayMs <= 0)
                {
                    await stream.WriteAsync(body);
                }
                else
                {
                    const int chunkSize = 256;
                    for (var offset = 0; offset < body.Length; offset += chunkSize)
                    {
                        var count = Math.Min(chunkSize, body.Length - offset);
                        await stream.WriteAsync(body.AsMemory(offset, count));
                        await stream.FlushAsync();
                        await Task.Delay(chunkDelayMs);
                    }
                }
                await stream.FlushAsync();
            }
            catch
            {
                // 客户端取消/断开时忽略
            }
        }

        private static async Task ReadRequestHeadersAsync(NetworkStream stream)
        {
            var buffer = new byte[8192];
            var accumulated = new List<byte>();
            while (accumulated.Count < 64 * 1024)
            {
                var read = await stream.ReadAsync(buffer.AsMemory());
                if (read <= 0) return;
                for (var i = 0; i < read; i++) accumulated.Add(buffer[i]);
                for (var i = 3; i < accumulated.Count; i++)
                {
                    if (accumulated[i - 3] == 13 && accumulated[i - 2] == 10 &&
                        accumulated[i - 1] == 13 && accumulated[i] == 10)
                    {
                        return;
                    }
                }
            }
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { }
            try { _serveTask.Wait(TimeSpan.FromSeconds(5)); } catch { }
        }
    }

    private static byte[] MakeBody(int size)
    {
        var body = new byte[size];
        Random.Shared.NextBytes(body);
        return body;
    }

    private static string NewTempDir() => Directory.CreateTempSubdirectory("musync-dl-test-").FullName;

    [Fact]
    public async Task Download_Success_WritesVerifiedPackage()
    {
        var body = MakeBody(4096);
        using var server = new OneShotServer(body);
        var dir = NewTempDir();
        try
        {
            var asset = new UpdateChecker.UpdateAsset
            {
                Name = "MuSync.exe",
                Size = body.Length,
                DownloadUrl = server.Url
            };
            var result = await UpdateDownloader.DownloadAsync(
                asset, "0.2.1", null, CancellationToken.None, dir);

            Assert.Equal(UpdateDownloader.DownloadStatus.Completed, result.Status);
            var package = Path.Combine(dir, "MuSync-v0.2.1.exe");
            Assert.True(File.Exists(package));
            Assert.Equal(body, await File.ReadAllBytesAsync(package));
            Assert.Empty(Directory.GetFiles(dir, "*.part"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Download_SizeMismatch_FailsAndCleansTemp()
    {
        var body = MakeBody(2048);
        using var server = new OneShotServer(body);
        var dir = NewTempDir();
        try
        {
            // 元数据声明的大小与实际不符 → 校验失败，不转正
            var asset = new UpdateChecker.UpdateAsset
            {
                Name = "MuSync.exe",
                Size = body.Length + 1,
                DownloadUrl = server.Url
            };
            var result = await UpdateDownloader.DownloadAsync(
                asset, "0.2.1", null, CancellationToken.None, dir);

            Assert.Equal(UpdateDownloader.DownloadStatus.Failed, result.Status);
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Download_HttpError_FailsAndCleansTemp()
    {
        var body = MakeBody(512);
        using var server = new OneShotServer(body, statusCode: 500);
        var dir = NewTempDir();
        try
        {
            var asset = new UpdateChecker.UpdateAsset
            {
                Name = "MuSync.exe",
                Size = body.Length,
                DownloadUrl = server.Url
            };
            var result = await UpdateDownloader.DownloadAsync(
                asset, "0.2.1", null, CancellationToken.None, dir);

            Assert.Equal(UpdateDownloader.DownloadStatus.Failed, result.Status);
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Download_Canceled_CleansTemp()
    {
        var body = MakeBody(64 * 1024);
        // 慢速分块发送：保证下载途中取消
        using var server = new OneShotServer(body, chunkDelayMs: 50);
        var dir = NewTempDir();
        try
        {
            var asset = new UpdateChecker.UpdateAsset
            {
                Name = "MuSync-lite.exe",
                Size = body.Length,
                DownloadUrl = server.Url
            };
            using var cts = new CancellationTokenSource();
            var progress = new InlineProgress(_ =>
            {
                if (!cts.IsCancellationRequested) cts.Cancel();
            });
            var result = await UpdateDownloader.DownloadAsync(asset, "0.2.1", progress, cts.Token, dir);

            Assert.Equal(UpdateDownloader.DownloadStatus.Canceled, result.Status);
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
