using System;
using System.Collections.Generic;
using System.Globalization;

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

    /// <summary>按 Unicode 文本元素切分，过滤纯空白字符（最多扫描 MaxScanElements 个）。</summary>
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
            if (!string.IsNullOrWhiteSpace(element))
            {
                elements.Add(element);
            }
        }
        return elements;
    }
}
