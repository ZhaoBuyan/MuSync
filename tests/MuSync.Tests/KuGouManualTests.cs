using System;
using System.Threading.Tasks;
using MuSync.Players;
using Xunit;
using Xunit.Abstractions;

namespace MuSync.Tests;

/// <summary>
/// 酷狗支持的"手动集成测试"：需要本机开着酷狗音乐（且正在播放）时才有意义。
/// 默认直接通过（CI / 日常测试运行不受影响）；
/// 本地手动验证：设置环境变量 MUSYNC_MANUAL_KUGOU=1 后再跑本测试，可看到读到的播放信息。
/// </summary>
public class KuGouManualTests
{
    private readonly ITestOutputHelper _output;

    public KuGouManualTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Probe_KuGou_Reports_Player_Info()
    {
        if (Environment.GetEnvironmentVariable("MUSYNC_MANUAL_KUGOU") != "1")
        {
            _output.WriteLine("未设置 MUSYNC_MANUAL_KUGOU=1，跳过酷狗手动集成测试。");
            return;
        }

        var process = KuGou.FindMainProcess();
        _output.WriteLine(process is null
            ? "[1] 主进程: 未找到（窗口标题不含「酷狗音乐」？）"
            : $"[1] 主进程: {process.ProcessName} (PID {process.Id}) 窗口「{process.MainWindowTitle}」");
        Assert.NotNull(process);

        var player = new KuGou(process!.Id);
        var info = await player.GetPlayerInfoAsync();
        if (info is null)
        {
            _output.WriteLine("[2] GetPlayerInfoAsync 返回 null（未读到酷狗信息）");
        }
        else
        {
            var playerInfo = info.Value;
            _output.WriteLine(
                $"[2] 读到: {playerInfo.Title} - {playerInfo.Artists} | {playerInfo.Schedule:F0}s / {playerInfo.Duration:F0}s | Pause={playerInfo.Pause}");
        }

        Assert.NotNull(info);
        Assert.False(string.IsNullOrWhiteSpace(info!.Value.Title));
    }
}
