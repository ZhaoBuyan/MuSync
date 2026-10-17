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
    Custom,
    /// <summary>自定义数据源积木（如心率）：<see cref="FormatBlock.Text"/> 存数据源名称。</summary>
    DataSource
}

/// <summary>模板积木块：类型 + 自定义文字（DataSource 类型时存数据源名称）。</summary>
internal sealed class FormatBlock
{
    public FormatBlockType Type { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>
/// 模板字符串与积木块列表互转。
/// 模板字符串沿用 {song} {artist} {artistPart} {progress} {app} {sep} 占位符，
/// 以及自定义数据源的 {名称}；块编辑器负责把用户搭的积木编译回该格式，
/// 保持存储与渲染引擎不变。
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

    /// <summary>
    /// 把模板字符串解析为积木块列表（连续普通文字合并为一个自定义块）。
    /// <para>
    /// 认不出的 <c>{名字}</c> 一律解析成 <see cref="FormatBlockType.DataSource"/> 块 ——
    /// 与渲染引擎"任意无括号名字都是积木"的规则保持一致，
    /// 这样用户手打的 <c>{心率}</c> 在编辑器里也能作为积木被看见、被排序、被删除，
    /// 而不是混在一段自定义文字里。
    /// </para>
    /// </summary>
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
            if (matched) continue;

            // 非内建标记：看是不是一个 {名字} 形状的积木（与模板引擎的匹配范围一致）
            if (text[i] == '{' && TryReadBlockName(text, i, out var name, out var length))
            {
                if (buffer.Length > 0)
                {
                    blocks.Add(new FormatBlock { Type = FormatBlockType.Custom, Text = buffer.ToString() });
                    buffer.Clear();
                }
                blocks.Add(new FormatBlock { Type = FormatBlockType.DataSource, Text = name });
                i += length;
                continue;
            }

            buffer.Append(text[i]);
            i++;
        }
        if (buffer.Length > 0)
        {
            blocks.Add(new FormatBlock { Type = FormatBlockType.Custom, Text = buffer.ToString() });
        }
        return blocks;
    }

    /// <summary>
    /// 从 <paramref name="start"/> 处的 <c>{</c> 读出一个积木名。
    /// 规则与 <see cref="SteamStatusManager"/> 的占位符正则一致：名字 1~32 字符、不含大括号与换行。
    /// </summary>
    private static bool TryReadBlockName(string text, int start, out string name, out int length)
    {
        name = "";
        length = 0;
        var end = text.IndexOf('}', start + 1);
        if (end < 0) return false;
        var inner = text[(start + 1)..end];
        if (inner.Length == 0 || inner.Length > SteamStatusManager.MaxBlockNameLength) return false;
        if (inner.Contains('{') || inner.Contains('\n') || inner.Contains('\r')) return false;
        name = inner;
        length = end - start + 1;
        return true;
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
                FormatBlockType.DataSource => "{" + block.Text + "}",
                _ => block.Text
            });
        }
        return sb.ToString();
    }
}
