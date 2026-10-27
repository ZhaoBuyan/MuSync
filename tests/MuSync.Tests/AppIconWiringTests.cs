using System.Drawing;
using MuSync;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 图标接线：应用图标（Resources/icon.ico）必须真的用上，而不是让某些窗口显示 WinForms 默认图标。
/// 之前设置窗口、积木编辑器、登录窗、更新窗都漏了设置 Icon。
/// </summary>
public class AppIconWiringTests
{
    [Fact]
    public void AppIcon_IsLoadedFromResources_NotTheSystemDefault()
    {
        var icon = AppResource.Icon;

        Assert.NotNull(icon);
        // 资源加载成功时不会是 SystemIcons.Application 这个实例
        Assert.NotSame(SystemIcons.Application, icon);
        Assert.True(icon.Width > 0 && icon.Height > 0);
    }

    [Fact]
    public void SettingsForm_UsesAppIcon()
    {
        using var form = new SettingsForm();

        Assert.Same(AppResource.Icon, form.Icon);
    }

    [Fact]
    public void FormatBlockEditorForm_UsesAppIcon()
    {
        using var form = new FormatBlockEditorForm(
            TemplateKind.Music, "{song}", " | ");

        Assert.Same(AppResource.Icon, form.Icon);
    }
}
