using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MuSync.Models;

namespace MuSync.Utils;

/// <summary>
/// 程序同步规则的导入 / 导出（纯函数，不碰界面、不碰 <see cref="Configurations"/> 单例，便于单测）。
/// 文件格式：JSON + 一层信封，便于以后演进：
/// <code>{ "format": "musync-app-rules", "version": 1, "exportedAt": "&lt;ISO8601&gt;", "rules": [ ... ] }</code>
/// 容错口径（硬要求）：文件不是 JSON / 缺 rules / 枚举串不认识 / ExeName 为空 → 跳过该条并计入「忽略」，
/// 绝不半途抛异常；合并策略固定为「只补新、不动已有」（按 ExeName 大小写不敏感匹配）。
/// </summary>
internal static class AppRuleExchange
{
    /// <summary>信封标识，导入时用来确认文件确实是本程序导出的规则。</summary>
    public const string FormatTag = "musync-app-rules";
    /// <summary>信封版本号（仅 1，将来加字段时递增）。</summary>
    public const int FormatVersion = 1;

    /// <summary>导出信封。字段用 camelCase，与配置文件的 PascalCase 有意区分：这是对外交换格式。</summary>
    private sealed class ExportEnvelope
    {
        [JsonPropertyName("format")] public string Format { get; set; } = FormatTag;
        [JsonPropertyName("version")] public int Version { get; set; } = FormatVersion;
        [JsonPropertyName("exportedAt")] public string ExportedAt { get; set; } = "";
        [JsonPropertyName("rules")] public List<AppRule> Rules { get; set; } = [];
    }

    /// <summary>导入信封：全部可空，缺字段只记警告不抛（宽容解析）。</summary>
    private sealed class ImportEnvelope
    {
        [JsonPropertyName("format")] public string? Format { get; set; }
        [JsonPropertyName("version")] public int? Version { get; set; }
        [JsonPropertyName("exportedAt")] public string? ExportedAt { get; set; }
        [JsonPropertyName("rules")] public List<AppRule?>? Rules { get; set; }
    }

    /// <summary>导入结果：合并后的列表 + 三类计数（摘要弹窗直接用它）+ 忽略原因（诊断用）。</summary>
    public sealed class ImportResult
    {
        /// <summary>合并后的完整规则列表（已含原有条目）。</summary>
        public List<AppRule> Merged { get; init; } = [];
        /// <summary>新增条数。</summary>
        public int Added { get; init; }
        /// <summary>跳过条数（本地已有同 ExeName，保留本地）。</summary>
        public int Skipped { get; init; }
        /// <summary>忽略条数（格式坏 / ExeName 为空）。</summary>
        public int Ignored { get; init; }
        /// <summary>整份文件不可用时的原因（非空即失败，其余计数无意义）。</summary>
        public string? Error { get; init; }
        /// <summary>被忽略条目的原因明细（日志用，不进弹窗）。</summary>
        public List<string> IgnoredReasons { get; init; } = [];

        /// <summary>整份文件是否可用。</summary>
        public bool Ok => Error == null;
    }

    /// <summary>把规则导出到文件（UTF-8 无 BOM，缩进 + 枚举转字符串，便于用户手工看）。</summary>
    public static void Export(string path, IEnumerable<AppRule> rules, DateTime nowLocal)
    {
        var envelope = new ExportEnvelope
        {
            ExportedAt = nowLocal.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            Rules = rules.ToList()
        };
        var json = JsonSerializer.Serialize(envelope, ExportOptions);
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    /// <summary>
    /// 把文件里的规则合并进 <paramref name="local"/>（不修改入参，返回新列表）。
    /// 只补新、不动已有；坏条目跳过并计入「忽略」。
    /// </summary>
    public static ImportResult Import(string path, IEnumerable<AppRule> local)
    {
        var merged = local.Select(CloneRule).ToList();
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            return new ImportResult { Error = $"读取文件失败：{ex.Message}" };
        }

        ImportEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ImportEnvelope>(text, ExportOptions);
        }
        catch (Exception ex)
        {
            return new ImportResult { Error = $"不是有效的规则文件（JSON 解析失败）：{ex.Message}" };
        }

        if (envelope?.Rules == null)
        {
            return new ImportResult { Error = "文件里没有 rules 列表，可能不是本程序导出的规则文件。" };
        }

        var reasons = new List<string>();
        var added = 0;
        var skipped = 0;
        var ignored = 0;

        for (var i = 0; i < envelope.Rules.Count; i++)
        {
            var rule = envelope.Rules[i];
            if (rule == null)
            {
                ignored++;
                reasons.Add($"第 {i + 1} 条：条目为空");
                continue;
            }
            var exeName = rule.ExeName?.Trim() ?? "";
            if (exeName.Length == 0)
            {
                ignored++;
                reasons.Add($"第 {i + 1} 条：ExeName 为空");
                continue;
            }
            if (merged.Any(r => r.ExeName.Equals(exeName, StringComparison.OrdinalIgnoreCase)))
            {
                skipped++;
                continue;
            }
            rule.ExeName = exeName;   // 顺手去掉首尾空白，保证后续比较口径一致
            merged.Add(rule);
            added++;
        }

        return new ImportResult
        {
            Merged = merged,
            Added = added,
            Skipped = skipped,
            Ignored = ignored,
            IgnoredReasons = reasons
        };
    }

    /// <summary>逐字段拷贝，避免共享引用（与 SettingsForm.CloneRule 同一口径）。</summary>
    private static AppRule CloneRule(AppRule source) => new()
    {
        ExeName = source.ExeName,
        DisplayName = source.DisplayName,
        Category = source.Category,
        Mode = source.Mode,
        Override = source.Override,
        Enabled = source.Enabled,
        IsUserConfirmed = source.IsUserConfirmed
    };

    /// <summary>
    /// 对外交换格式的 JSON 选项：缩进 + 枚举按字符串 + 中文不转义。
    /// 中文若按默认转义成 \uXXXX，用户手工查看 / 编辑规则文件会很难认，所以用宽松编码器
    /// （只影响可读性，转义与不转义往返等价，由 ExportImportExport 一致性测试兜住）。
    /// </summary>
    private static readonly JsonSerializerOptions ExportOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };
}
