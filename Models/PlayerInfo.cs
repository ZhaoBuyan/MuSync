namespace MuSync.Models;
/// <summary>播放器统一信息模型：歌名 / 歌手 / 专辑 / 封面 / 进度 / 时长 / 暂停状态等。</summary>
internal readonly record struct PlayerInfo
{
    public required string Identity { get; init; }
    public required string Title { get; init; }
    public required string Artists { get; init; }
    public required string Album { get; init; }
    public required string Cover { get; init; }
    public required double Schedule { get; init; }
    public required double Duration { get; init; }
    public required string Url { get; init; }
    public required bool Pause { get; init; }
}