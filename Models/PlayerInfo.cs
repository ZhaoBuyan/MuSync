namespace MuSync.Models;
/// <summary>播放器统一信息模型：歌名 / 歌手 / 专辑 / 封面 / 进度 / 时长 / 暂停状态等。</summary>
internal readonly record struct PlayerInfo
{
    public required string Identity { get; init; }
    public required string Title { get; init; }
    public required string Artists { get; init; }
    public required string Album { get; init; }
    public required string Cover { get; init; }
    /// <summary>
    /// 封面图片字节（PNG / JPEG 原样）：用于"不走 URL 下载"的封面来源。
    /// 目前来自系统媒体控件（SMTC）的缩略图 —— 浏览器播视频时是视频海报、音乐播放器是专辑封面。
    /// 为 null 表示没有这类封面，界面退回 <see cref="Cover"/> 的 URL 或图标占位。
    /// </summary>
    public byte[]? CoverImage { get; init; }
    public required double Schedule { get; init; }
    public required double Duration { get; init; }
    public required string Url { get; init; }
    public required bool Pause { get; init; }
}