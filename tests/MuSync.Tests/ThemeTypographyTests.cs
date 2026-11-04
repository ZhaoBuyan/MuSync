using System.Linq;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 排版守卫：字体层级与字体栈必须与 `docs/UI-REDESIGN.md` §3.2 一致。
/// <para>
/// 为什么值得一条测试：字号层级是这个界面唯一的"排版信息"（没有卡片、没有间距分区可用），
/// 一旦有人把 Heading 调成和 Section 一样大，缩略后所有分区标题就失去层级 —— 而肉眼很难发现"少了半级"。
/// 另一条是字体栈：文档已明确"不内置字体、用系统字体"，若有人把 Body 改成别的族却没同步文档，
/// 这条测试会失败并指向文档位置。
/// </para>
/// </summary>
public class ThemeTypographyTests
{
    [Fact]
    public void FontScaleIsStrictlyOrdered()
    {
        Assert.True(Theme.Heading.SizeInPoints > Theme.Section.SizeInPoints,
            $"Heading({Theme.Heading.SizeInPoints}) 必须大于 Section({Theme.Section.SizeInPoints})");
        Assert.True(Theme.Section.SizeInPoints > Theme.Body.SizeInPoints,
            $"Section({Theme.Section.SizeInPoints}) 必须大于 Body({Theme.Body.SizeInPoints})");
        Assert.True(Theme.Body.SizeInPoints > Theme.Caption.SizeInPoints,
            $"Body({Theme.Body.SizeInPoints}) 必须大于 Caption({Theme.Caption.SizeInPoints})");
        Assert.True(Theme.Output.SizeInPoints > Theme.Data.SizeInPoints,
            $"Output({Theme.Output.SizeInPoints}) 必须大于 Data({Theme.Data.SizeInPoints})");
    }

    [Fact]
    public void SizeStepsAreVisibleNotSubtle()
    {
        // 文档要求：层级差要明显（Body 9.5 → Section 12 → Heading 16），不要 400↔600 那种"看不出来"的差
        Assert.True(Theme.Section.SizeInPoints / Theme.Body.SizeInPoints >= 1.2,
            "Section 相对 Body 的提升不足 20%，层级会看不出来");
        Assert.True(Theme.Heading.SizeInPoints / Theme.Section.SizeInPoints >= 1.2,
            "Heading 相对 Section 的提升不足 20%，层级会看不出来");
    }

    [Fact]
    public void DataRolesUseTheSameMonospaceFamily()
    {
        var dataFamily = Theme.Data.FontFamily.Name;
        Assert.Equal(dataFamily, Theme.Output.FontFamily.Name);
        Assert.NotEqual(Theme.Body.FontFamily.Name, dataFamily);
    }

    [Fact]
    public void FontStackMatchesDocumentedChoice()
    {
        // 若这条失败：要么改回去，要么同步更新 docs/UI-REDESIGN.md §3.2 的字体表与"为什么不用 Archivo"段落
        Assert.Equal("Segoe UI", Theme.Body.FontFamily.Name);
        Assert.Equal("Consolas", Theme.Data.FontFamily.Name);
    }
}
