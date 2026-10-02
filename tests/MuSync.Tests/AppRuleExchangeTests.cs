using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MuSync.Models;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>应用规则导入 / 导出的纯逻辑测试：信封往返、只补新合并、坏文件与坏条目容错、再导出一致。</summary>
public class AppRuleExchangeTests : IDisposable
{
    private readonly string _dir;

    public AppRuleExchangeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"musync-rules-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 临时目录清理失败不影响断言 */ }
    }

    private string PathOf(string name) => Path.Combine(_dir, name);

    private static AppRule Rule(
        string exe, string display = "", AppCategory category = AppCategory.Other,
        AppSyncMode mode = AppSyncMode.Foreground, AppSyncOverride over = AppSyncOverride.FollowCategory,
        bool enabled = true, bool confirmed = true) => new()
    {
        ExeName = exe,
        DisplayName = display,
        Category = category,
        Mode = mode,
        Override = over,
        Enabled = enabled,
        IsUserConfirmed = confirmed
    };

    private static readonly DateTime FixedNow = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Local);

    [Fact]
    public void Export_WritesEnvelopeWithFormatAndVersion()
    {
        var path = PathOf("only.json");
        AppRuleExchange.Export(path, [Rule("a.exe", "A")], FixedNow);

        var text = File.ReadAllText(path);
        Assert.Contains(AppRuleExchange.FormatTag, text);
        Assert.Contains("\"version\": 1", text);
        Assert.Contains("\"exportedAt\"", text);
        Assert.Contains("2026-09-30T12:00:00", text);
    }

    [Fact]
    public void Export_UsesStringEnumsAndStaysReadable()
    {
        var path = PathOf("enums.json");
        AppRuleExchange.Export(path, [Rule("a.exe", "A", AppCategory.Game, AppSyncMode.Always, AppSyncOverride.ForceOn, false, false)], FixedNow);

        var text = File.ReadAllText(path);
        // 字符串枚举（不是 0/1/2），用户手工可读、跨版本稳定
        Assert.Contains("\"Game\"", text);
        Assert.Contains("\"Always\"", text);
        Assert.Contains("\"ForceOn\"", text);
        Assert.DoesNotContain("\"Category\": 1", text);
    }

    [Fact]
    public void Export_NoBomAndUtf8()
    {
        var path = PathOf("bom.json");
        AppRuleExchange.Export(path, [Rule("网易云音乐.exe", "网易云音乐")], FixedNow);

        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "不应写 BOM");
        Assert.Contains("网易云音乐", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrip_AllSevenFieldsSurvive()
    {
        var path = PathOf("roundtrip.json");
        var original = Rule("strinova.exe", "Strinova", AppCategory.Game, AppSyncMode.Always, AppSyncOverride.ForceOff, false, false);
        AppRuleExchange.Export(path, [original], FixedNow);

        var result = AppRuleExchange.Import(path, []);

        Assert.True(result.Ok, result.Error);
        var loaded = Assert.Single(result.Merged);
        Assert.Equal(original.ExeName, loaded.ExeName);
        Assert.Equal(original.DisplayName, loaded.DisplayName);
        Assert.Equal(original.Category, loaded.Category);
        Assert.Equal(original.Mode, loaded.Mode);
        Assert.Equal(original.Override, loaded.Override);
        Assert.Equal(original.Enabled, loaded.Enabled);
        Assert.Equal(original.IsUserConfirmed, loaded.IsUserConfirmed);
        Assert.Equal(1, result.Added);
    }

    [Fact]
    public void Import_NewAppsAreAppended_LocalKeptIntact()
    {
        var path = PathOf("merge.json");
        AppRuleExchange.Export(path, [Rule("new.exe", "New"), Rule("local.exe", "FileVersion")], FixedNow);

        var local = new List<AppRule> { Rule("local.exe", "LocalVersion", AppCategory.Media) };
        var result = AppRuleExchange.Import(path, local);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(2, result.Merged.Count);
        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Skipped);
        // 本地已有条目必须原样保留（不被文件覆盖）
        var kept = result.Merged.Single(r => r.ExeName == "local.exe");
        Assert.Equal("LocalVersion", kept.DisplayName);
        Assert.Equal(AppCategory.Media, kept.Category);
        // 入参不被修改（纯函数）
        Assert.Single(local);
    }

    [Fact]
    public void Import_ExeNameMatchIsCaseInsensitive()
    {
        var path = PathOf("case.json");
        AppRuleExchange.Export(path, [Rule("StrInova.EXE", "FromFile")], FixedNow);

        var result = AppRuleExchange.Import(path, [Rule("strinova.exe", "Local")]);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Skipped);
        Assert.Equal("Local", Assert.Single(result.Merged).DisplayName);
    }

    [Fact]
    public void Import_DuplicateInsideFile_SecondOneSkipped()
    {
        var path = PathOf("dup.json");
        AppRuleExchange.Export(path, [Rule("a.exe", "First"), Rule("A.EXE", "Second")], FixedNow);

        var result = AppRuleExchange.Import(path, []);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Skipped);
        Assert.Equal("First", Assert.Single(result.Merged).DisplayName);
    }

    [Fact]
    public void Import_NotJson_ReportsErrorInsteadOfThrowing()
    {
        var path = PathOf("broken.json");
        File.WriteAllText(path, "这根本不是 JSON {{{", new UTF8Encoding(false));

        var result = AppRuleExchange.Import(path, []);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.Empty(result.Merged);
    }

    [Fact]
    public void Import_MissingRulesArray_ReportsErrorInsteadOfThrowing()
    {
        var path = PathOf("norules.json");
        File.WriteAllText(path, "{ \"format\": \"musync-app-rules\", \"version\": 1 }", new UTF8Encoding(false));

        var result = AppRuleExchange.Import(path, []);

        Assert.False(result.Ok);
        Assert.Contains("rules", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_MissingFile_ReportsErrorInsteadOfThrowing()
    {
        var result = AppRuleExchange.Import(PathOf("nope.json"), []);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Import_UnknownEnumString_DoesNotThrow()
    {
        // 枚举串不认识：整条被外层兜住（宽容解析口径），绝不半途抛
        var path = PathOf("badenum.json");
        File.WriteAllText(path, """
        { "format": "musync-app-rules", "version": 1, "rules": [
            { "ExeName": "bad.exe", "Category": "NotACategory" } ] }
        """, new UTF8Encoding(false));

        var result = AppRuleExchange.Import(path, []);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.Empty(result.Merged);
    }

    [Fact]
    public void Import_EmptyExeName_CountedAsIgnored()
    {
        var path = PathOf("emptyexe.json");
        File.WriteAllText(path, """
        { "format": "musync-app-rules", "version": 1, "rules": [
            { "ExeName": "  ", "DisplayName": "NoExe" },
            { "ExeName": "ok.exe", "DisplayName": "Ok" } ] }
        """, new UTF8Encoding(false));

        var result = AppRuleExchange.Import(path, []);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Ignored);
        Assert.Contains(result.IgnoredReasons, r => r.Contains("ExeName", StringComparison.Ordinal));
        Assert.Equal("ok.exe", Assert.Single(result.Merged).ExeName);
    }

    [Fact]
    public void Import_ExeNamePaddingTrimmed()
    {
        var path = PathOf("pad.json");
        File.WriteAllText(path, """
        { "format": "musync-app-rules", "version": 1, "rules": [
            { "ExeName": "  padded.exe  " } ] }
        """, new UTF8Encoding(false));

        var result = AppRuleExchange.Import(path, []);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("padded.exe", Assert.Single(result.Merged).ExeName);
    }

    [Fact]
    public void Import_IgnoresAndDisabledEntriesAreCarriedOver()
    {
        // 导出「全量现场」口径：忽略类与未启用条目也要能导回去
        var path = PathOf("allsite.json");
        AppRuleExchange.Export(path,
            [Rule("ignored.exe", "Ignored", AppCategory.Ignore), Rule("off.exe", "Off", enabled: false)], FixedNow);

        var result = AppRuleExchange.Import(path, []);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(2, result.Added);
        Assert.Contains(result.Merged, r => r.Category == AppCategory.Ignore);
        Assert.Contains(result.Merged, r => !r.Enabled);
    }

    [Fact]
    public void ExportImportExport_ProducesIdenticalRules()
    {
        var first = PathOf("first.json");
        var second = PathOf("second.json");
        var rules = new List<AppRule>
        {
            Rule("a.exe", "A", AppCategory.Game),
            Rule("b.exe", "B", AppCategory.Work, AppSyncMode.Always, AppSyncOverride.ForceOff, false, false)
        };

        AppRuleExchange.Export(first, rules, FixedNow);
        var imported = AppRuleExchange.Import(first, []);
        AppRuleExchange.Export(second, imported.Merged, FixedNow);

        var a = File.ReadAllText(first);
        var b = File.ReadAllText(second);
        Assert.Equal(a, b);
    }
}
