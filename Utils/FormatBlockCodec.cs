using System.Collections.Generic;
using System.Text;
namespace MuSync.Utils;

/// <summary>可变模板的一个"积木块"类型。</summary>
internal enum FormatBlockType
{
    Song,
    Artist,
    ArtistPart,
    Progress,
    App,
    Sep,
    Custom
}

/// <summary>模板积木块：类型 + 自定义文字。</summary>
internal sealed class FormatBlock
{
    public FormatBlockType Type { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>
/// 模板字符串与积木块列表互转。
/// 模板字符串沿用 {song} {artist} {artistPart} {progress} {app} {sep} 占位符，
/// 块编辑器负责把用户搭的积木编译回该格式，保持存储与渲染引擎不变。
/// </summary>
internal static class FormatBlockCodec
{
    private static readonly (string Token, FormatBlockType Type)[] Tokens =
    [
        ("{artistPart}", FormatBlockType.ArtistPart),
        ("{progress}", FormatBlockType.Progress),
        ("{artist}", FormatBlockType.Artist),
        ("{song}", FormatBlockType.Song),
        ("{sep}", FormatBlockType.Sep),
        ("{app}", FormatBlockType.App)
    ];

    /// <summary>把模板字符串解析为积木块列表（连续普通文字合并为一个自定义块）。</summary>
    public static List<FormatBlock> Parse(string? format)
    {
        var blocks = new List<FormatBlock>();
        var text = format ?? "";
        var buffer = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            var matched = false;
            foreach (var (token, type) in Tokens)
            {
                if (i + token.Length <= text.Length &&
                    string.CompareOrdinal(text, i, token, 0, token.Length) == 0)
                {
                    if (buffer.Length > 0)
                    {
                        blocks.Add(new FormatBlock { Type = FormatBlockType.Custom, Text = buffer.ToString() });
                        buffer.Clear();
                    }
                    blocks.Add(new FormatBlock { Type = type });
                    i += token.Length;
                    matched = true;
                    break;
                }
            }
            if (!matched)
            {
                buffer.Append(text[i]);
                i++;
            }
        }
        if (buffer.Length > 0)
        {
            blocks.Add(new FormatBlock { Type = FormatBlockType.Custom, Text = buffer.ToString() });
        }
        return blocks;
    }

    /// <summary>把积木块列表编译回模板字符串。</summary>
    public static string Compile(IEnumerable<FormatBlock> blocks)
    {
        var sb = new StringBuilder();
        foreach (var block in blocks)
        {
            sb.Append(block.Type switch
            {
                FormatBlockType.Song => "{song}",
                FormatBlockType.Artist => "{artist}",
                FormatBlockType.ArtistPart => "{artistPart}",
                FormatBlockType.Progress => "{progress}",
                FormatBlockType.App => "{app}",
                FormatBlockType.Sep => "{sep}",
                _ => block.Text
            });
        }
        return sb.ToString();
    }
}
