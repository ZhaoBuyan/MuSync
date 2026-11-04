using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

public class UpdateTransferGuardTests
{
    [Fact]
    public async Task BodyThatStallsAfterHeadersTimesOutAndCleansTemporaryFile()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var headersSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(lifetime.Token);
            await using var stream = client.GetStream();
            using var request = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            while (!string.IsNullOrEmpty(await request.ReadLineAsync(lifetime.Token))) { }
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 20\r\nConnection: close\r\n\r\n"), lifetime.Token);
            headersSent.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, lifetime.Token); }
            catch (OperationCanceledException) { }
        });
        var directory = Path.Combine(Path.GetTempPath(), "musync-stalled-download-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var asset = new UpdateChecker.UpdateAsset { Name = "MuSync.exe", Size = 20,
                DownloadUrl = $"http://127.0.0.1:{port}/MuSync.exe" };
            var download = UpdateDownloader.DownloadAsync(asset, "1.0.0", null, CancellationToken.None,
                directory, TimeSpan.FromSeconds(1));
            await headersSent.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var result = await download.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(UpdateDownloader.DownloadStatus.Failed, result.Status);
            Assert.NotEmpty(result.ErrorMessage);
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            lifetime.Cancel();
            await server;
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ExpectedDigestCannotPassWhenFileIsUnreadable()
    {
        Assert.False(UpdateDownloader.VerifySha256(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
            "sha256:" + new string('a', 64)));
    }
}
