using System;
using System.Text;
namespace MuSync.Utils;
/// <summary>
/// 更新日志文本处理：GitHub Release 正文是 markdown（标题井号、加粗星号等），
/// 而更新窗是纯文本框、不渲染标记——展示前先转成干净纯文本，别让用户看到 `###` / `**`。
/// </summary>
internal static class ReleaseNotes
{
    /// <summary>
    /// markdown → 纯文本：
    /// 去掉行首标题井号（`### 新增` → `新增`）与引用符号（`&gt; `）；去掉加粗标记 `**`；
    /// 去掉行内代码反引号；压掉 3 行以上连续空行。列表符 `- ` 保留（纯文本下更易读）。
    /// 单个 `*` 一律不碰，避免误伤 `*.png` 这类内容。
    /// </summary>
    public static string ToPlainText(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        var source = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        var builder = new StringBuilder(source.Length);
        foreach (var rawLine in source.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('#'))
            {
                var hashes = 0;
                while (hashes < line.Length && line[hashes] == '#') hashes++;
                if (hashes <= 6 && hashes < line.Length && line[hashes] == ' ')
                {
                    line = line[(hashes + 1)..].TrimStart();
                }
            }
            else if (line == ">")
            {
                line = "";
            }
            else if (line.StartsWith("> ", StringComparison.Ordinal))
            {
                line = line[2..].TrimStart();
            }
            line = line.Replace("**", "").Replace("`", "");
            builder.Append(line).Append('\n');
        }
        var result = builder.ToString().Trim('\n');
        while (result.Contains("\n\n\n", StringComparison.Ordinal))
        {
            result = result.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
        }
        return result;
    }
}
