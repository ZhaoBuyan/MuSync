using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MuSync.Utils;

/// <summary>
/// 进度条样式解析：按「用户可见字符」（Unicode 文本元素）切分，
/// emoji 及其变体选择符（如 ❤️ = U+2764 + U+FE0F）不会被拆开。
/// </summary>
internal static class BarStyleParser
{
    private const int MaxScanElements = 32;

    /// <summary>
    /// 解析「填充字符 + 空白字符」样式串：
    /// 取第一个可见字符作填充、第一个不同的可见字符作空白；
    /// 因此直接粘贴 ❤️❤️💕💕💕💕 这样的一串也能正确解析。
    /// </summary>
    public static (string Fill, string Empty) Parse(string? text)
    {
        var elements = SplitVisibleElements(text);
        string? fill = null;
        string? empty = null;
        foreach (var element in elements)
        {
            if (fill == null)
            {
                fill = element;
                continue;
            }
            if (empty == null && element != fill)
            {
                empty = element;
                break;
            }
        }
        // 输入全部为同一种字符（如 ██）时：第二个字符兼作空白，兼容旧行为
        if (empty == null && elements.Count >= 2)
        {
            empty = elements[1];
        }
        if (string.IsNullOrWhiteSpace(fill)) fill = "#";
        if (string.IsNullOrWhiteSpace(empty)) empty = "-";
        return (fill, empty);
    }

    /// <summary>取单个进度条字符（用于读取配置值）：取第一个可见字符，异常时回退默认。</summary>
    public static string Normalize(string? value, string fallback)
    {
        if (string.IsNullOrEmpty(value)) return fallback;
        var element = StringInfo.GetNextTextElement(value);
        return string.IsNullOrWhiteSpace(element) ? fallback : element;
    }

    /// <summary>判断一个文本元素是否“不可见”（空白 / 零宽控制字符），用于过滤粘贴时混入的杂质。
    /// 注意：emoji 序列内部的 ZWJ 会被合并进同一文本元素，不会受此影响。</summary>
    private static bool IsInvisibleElement(string element)
    {
        if (string.IsNullOrWhiteSpace(element)) return true;
        foreach (var rune in element.EnumerateRunes())
        {
            var isInvisible = rune.Value
                is 0x00AD       // 软连字符
                or 0x200B       // 零宽空格
                or 0x200C       // 零宽不连字
                or 0x200D       // 零宽连接符（孤立出现时）
                or 0x200E       // LRM
                or 0x200F       // RLM
                or 0x2060       // 单词连接符
                or 0xFEFF;      // BOM / 零宽不换行空格
            if (!isInvisible) return false;
        }
        return true;
    }

    /// <summary>按 Unicode 文本元素切分，过滤空白与零宽字符（最多扫描 MaxScanElements 个）。</summary>
    private static List<string> SplitVisibleElements(string? text)
    {
        var elements = new List<string>();
        if (string.IsNullOrEmpty(text)) return elements;
        var index = 0;
        while (index < text.Length && elements.Count < MaxScanElements)
        {
            var element = StringInfo.GetNextTextElement(text, index);
            if (element.Length == 0) break;
            index += element.Length;
            if (!IsInvisibleElement(element))
            {
                elements.Add(element);
            }
        }
        return elements;
    }
}
