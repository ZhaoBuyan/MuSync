using System;
using MuSync;
using MuSync.Models;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 播放器状态变化检测的回归测试。
/// <para>
/// 为什么不能只靠「进度推算」（实际走了多少 vs 应该走多少）发现换歌：
/// 只比较「播放器身份 + 暂停态」+ 位置推算时，SMTC 这类源的位置每约 5 秒才更新一次，
/// 推算偏差常常小于容差 → **换歌被误判成没变化，状态迟迟不更新**。
/// 因此新增「直接比较曲目信息」的判定，这组测试把它钉死。
/// </para>
/// </summary>
public class RpcManagerChangeDetectionTests
{
    private const double Tolerance = 0.4;   // 与 RpcManager.JumpToleranceSeconds 一致
    private static readonly DateTime T0 = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

    private static PlayerInfo Info(
        string title, string artist = "A", string album = "Al", double schedule = 10, double duration = 200,
        bool pause = false, string identity = "TTPlayer") => new()
    {
        Identity = identity,
        Title = title,
        Artists = artist,
        Album = album,
        Cover = "",
        Schedule = schedule,
        Duration = duration,
        Url = "",
        Pause = pause
    };

    [Fact]
    public void TitleChange_IsDetected()
    {
        // 关键回归：换歌时 SMTC 位置信息可能还是上一首的（5 秒级刷新），
        // 靠进度推算会漏判 —— 必须由曲目信息直接判定
        var changed = RpcManager.DetectStateChange(
            Info("New Song", schedule: 10),
            Info("Old Song", schedule: 300),
            T0.AddSeconds(1.2), T0, Tolerance);

        Assert.True(changed);
    }

    [Fact]
    public void ArtistChange_IsDetected()
    {
        var changed = RpcManager.DetectStateChange(
            Info("Same Title", artist: "Artist B"),
            Info("Same Title", artist: "Artist A"),
            T0.AddSeconds(1.2), T0, Tolerance);

        Assert.True(changed);
    }

    [Fact]
    public void AlbumChange_IsDetected()
    {
        var changed = RpcManager.DetectStateChange(
            Info("Same", album: "Album B"),
            Info("Same", album: "Album A"),
            T0.AddSeconds(1.2), T0, Tolerance);

        Assert.True(changed);
    }

    [Fact]
    public void SameTrackWithinStaleProgress_IsNotDetected()
    {
        // 同一首歌、位置没动（SMTC 5 秒内不给新位置）：不该反复判定为变化，否则会频繁重推
        var changed = RpcManager.DetectStateChange(
            Info("Same Song", schedule: 10),
            Info("Same Song", schedule: 10),
            T0.AddSeconds(1.2), T0, Tolerance);

        Assert.False(changed);
    }

    [Fact]
    public void SameTrackWithNormalAdvance_IsNotDetected()
    {
        // 正常播放：间隔 5 秒，位置前进约 5 秒（SMTC 的刷新节奏）
        var changed = RpcManager.DetectStateChange(
            Info("Same Song", schedule: 15),
            Info("Same Song", schedule: 10),
            T0.AddSeconds(5), T0, Tolerance);

        Assert.False(changed);
    }

    [Fact]
    public void SameTrackWithProgressJump_IsDetectedAfterEnoughElapsed()
    {
        // 同一首歌内拖动进度条：间隔够长时，位置跳变仍要被发现
        var changed = RpcManager.DetectStateChange(
            Info("Same Song", schedule: 120),
            Info("Same Song", schedule: 10),
            T0.AddSeconds(4), T0, Tolerance);

        Assert.True(changed);
    }

    [Fact]
    public void ShortIntervalPositionNoise_IsIgnored()
    {
        // 间隔很短（233ms 全速轮询）时，位置样本噪声不该被当成跳变
        var changed = RpcManager.DetectStateChange(
            Info("Same Song", schedule: 40),
            Info("Same Song", schedule: 10),
            T0.AddSeconds(0.233), T0, Tolerance);

        Assert.False(changed);
    }

    [Fact]
    public void PauseStateChange_IsDetected()
    {
        var changed = RpcManager.DetectStateChange(
            Info("Same Song", pause: true),
            Info("Same Song", pause: false),
            T0.AddSeconds(1), T0, Tolerance);

        Assert.True(changed);
    }

    /// <summary>
    /// 回归：同一首歌、同歌手、同专辑，**只有封面不同**时，
    /// 早先返回 False → 界面封面不会刷新（典型场景：封面迟到、换了专辑图）。
    /// </summary>
    [Fact]
    public void CoverChange_IsDetected()
    {
        var changed = RpcManager.DetectStateChange(
            Info("Same Song") with { Cover = "https://new-cover.jpg" },
            Info("Same Song") with { Cover = "https://old-cover.jpg" },
            T0.AddSeconds(1.2), T0, Tolerance);

        Assert.True(changed);
    }

    [Fact]
    public void CoverAppearingLater_IsDetected()
    {
        // 封面迟到（先只读到标题，随后拿到封面 URL）也要被察觉
        var changed = RpcManager.DetectStateChange(
            Info("Same Song") with { Cover = "https://cover.jpg" },
            Info("Same Song") with { Cover = "" },
            T0.AddSeconds(1.2), T0, Tolerance);

        Assert.True(changed);
    }

    [Fact]
    public void SameCover_IsNotDetected()
    {
        // 反向保护：封面没变时不许被当成变化，否则会反复重推
        var changed = RpcManager.DetectStateChange(
            Info("Same Song") with { Cover = "https://cover.jpg" },
            Info("Same Song") with { Cover = "https://cover.jpg" },
            T0.AddSeconds(1.2), T0, Tolerance);

        Assert.False(changed);
    }

    [Fact]
    public void PlayerIdentityChange_IsDetected()
    {
        var changed = RpcManager.DetectStateChange(
            Info("Song", identity: "TTPlayer"),
            Info("Song", identity: "NetEase"),
            T0.AddSeconds(1), T0, Tolerance);

        Assert.True(changed);
    }

    [Fact]
    public void InfoAppearingOrDisappearing_IsDetected()
    {
        Assert.True(RpcManager.DetectStateChange(Info("Song"), null, T0, T0, Tolerance));
        Assert.True(RpcManager.DetectStateChange(null, Info("Song"), T0, T0, Tolerance));
        Assert.False(RpcManager.DetectStateChange(null, null, T0, T0, Tolerance));
    }
}
