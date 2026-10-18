using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using MuSync;
using MuSync.Models;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 积木编辑器的真实窗体测试（不是纯函数测试）：验证「数据源」积木在界面上确实可用。
/// <para>
/// 这条守卫的由来：这块功能最初的缺口就是"编辑器里没有数据源这一项"，
/// 用户只能靠手打 <c>{心率}</c>，文档还写不明白。纯函数测试看不出这种缺口 ——
/// 必须真的把窗体建出来、看控件树里有没有那个下拉框。
/// </para>
/// </summary>
public class FormatBlockEditorFormTests
{
    private static FormatBlockEditorForm CreateForm(string format) =>
        new(TemplateKind.Music, format, "‖");

    /// <summary>深度找出所有指定类型的控件（行是动态加进 Panel 的，不能只查 Controls）。</summary>
    private static List<Control> AllControls(Control root)
    {
        var found = new List<Control>();
        var pending = new Stack<Control>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var control = pending.Pop();
            found.Add(control);
            foreach (Control child in control.Controls) pending.Push(child);
        }
        return found;
    }

    [Fact]
    public void BlockTypeList_OffersDataSource()
    {
        using var form = CreateForm("{song}");

        var combo = AllControls(form).OfType<ComboBox>()
            .First(c => c.Name != "BlockDataSourceBox");

        // 9 种积木：歌名/歌手/歌手（无连接符）/进度条/时间轴/程序名/分隔符/自定义文字/数据源
        Assert.Equal(9, combo.Items.Count);
        Assert.Contains(combo.Items.Cast<object>(), item => item?.ToString() == Loc.L("数据源", "Data source"));
        Assert.Contains(combo.Items.Cast<object>(), item => item?.ToString() == Loc.L("时间轴", "Timeline"));
    }

    [Fact]
    public void HandWrittenDataSourceToken_ShowsUpAsADataSourceBlock()
    {
        // 用户手打 {心率} → 打开编辑器后必须是一块"数据源"积木（有下拉框），而不是一段文字
        using var form = CreateForm("{song} {心率}");

        var editors = AllControls(form).OfType<ComboBox>()
            .Where(c => c.Name == "BlockDataSourceBox")
            .ToList();

        Assert.Single(editors);
        Assert.Equal("心率", editors[0].Text);
    }

    [Fact]
    public void DataSourceBlock_IsEditableAndKeepsName()
    {
        using var form = CreateForm("{心率}");

        var editor = AllControls(form).OfType<ComboBox>().First(c => c.Name == "BlockDataSourceBox");
        // 可编辑（既能选已配置的，也能手打）
        Assert.NotEqual(ComboBoxStyle.DropDownList, editor.DropDownStyle);
        Assert.Equal("心率", editor.Text);
    }

    [Fact]
    public void DataSourceDropdown_ListsConfiguredSources()
    {
        // 配置里放两个源，编辑器下拉里应该能选到
        var original = Configurations.Instance.Settings.DataSources;
        try
        {
            Configurations.Instance.Settings.DataSources =
            [
                new DataSourceConfig { Name = "心率", Url = "http://127.0.0.1:9000/heartrate", ValuePath = "heart_rate" },
                new DataSourceConfig { Name = "步数", Url = "http://127.0.0.1:9001/steps", ValuePath = "steps" }
            ];

            using var form = CreateForm("{心率}");
            var editor = AllControls(form).OfType<ComboBox>().First(c => c.Name == "BlockDataSourceBox");
            var items = editor.Items.Cast<object>().Select(i => i?.ToString()).ToList();

            Assert.Contains("心率", items);
            Assert.Contains("步数", items);
        }
        finally
        {
            Configurations.Instance.Settings.DataSources = original;
        }
    }

    [Fact]
    public void OkButton_RecompilesToTheSameFormat()
    {
        // 打开再点确定，不能改动用户原有的模板
        const string format = "{app} {sep} {song} {心率}";
        using var form = CreateForm(format);

        var ok = AllControls(form).OfType<Button>()
            .First(b => b.Text == Loc.L("确定", "OK"));
        // ⚠️ 不能用 PerformClick()：窗体未显示时 CanSelect 为 false，点击会被忽略。
        // 这里直接调用受保护的 OnClick，等价于用户点击。
        var onClick = typeof(Button).GetMethod("OnClick",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(onClick);
        onClick!.Invoke(ok, [System.EventArgs.Empty]);

        Assert.Equal(format, form.ResultFormat);
    }
}
