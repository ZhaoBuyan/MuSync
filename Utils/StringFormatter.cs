using System.Collections.Concurrent;
using System.Text;
using MuSync.Models;
namespace MuSync.Utils;
/// <summary>StringBuilder 轻量复用池（降低高频拼接的分配开销）。</summary>
internal static class StringBuilderPool
{
    private static readonly ConcurrentQueue<StringBuilder> Pool = new();
    private const int MaxPoolSize = 10;
    public static StringBuilder Get()
    {
        if (!Pool.TryDequeue(out var sb)) return new StringBuilder(256); 
        sb.Clear();
        return sb;
    }
    public static void Return(StringBuilder sb)
    {
        if (sb.Capacity <= 512 && Pool.Count < MaxPoolSize)
        {
            Pool.Enqueue(sb);
        }
    }
}
/// <summary>状态文本格式化：含零宽字符处理，生成 Steam 显示用的标题 / 歌手文本。</summary>
internal static class StringFormatter
{
    private const char ZeroWidthSpace = '\u200B';
    public static (string Title, string Artists) FormatPlayerInfo(
        PlayerInfo info, 
        Configurations config)
    {
        var sb = StringBuilderPool.Get();
        try
        {
            sb.Append(info.Title)
              .Append(ZeroWidthSpace);
            var formattedTitle = StringUtils.GetTruncatedStringByMaxByteLength(sb.ToString(), 128);
            sb.Clear();
            sb.Append(info.Artists)
              .Append(ZeroWidthSpace);
            var formattedArtists = StringUtils.GetTruncatedStringByMaxByteLength(sb.ToString(), 128);
            return (formattedTitle, formattedArtists);
        }
        finally
        {
            StringBuilderPool.Return(sb);
        }
    }
}