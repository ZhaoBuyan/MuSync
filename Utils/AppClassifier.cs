using System;
using System.Collections.Generic;
using System.IO;
using MuSync.Models;
namespace MuSync.Utils;

/// <summary>
/// 本地启发式程序分类器（不联网、无依赖）：
/// 内置常见程序字典 + 关键词规则 + 全屏检测，给新发现的程序一个分类建议，
/// 最终分类由用户在设置中确认/调整。
/// </summary>
internal static class AppClassifier
{
    /// <summary>内置忽略名单：系统外壳/输入法等，出现在前台时不产生任何动作。</summary>
    private static readonly HashSet<string> BuiltInIgnored = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "dwm.exe", "taskmgr.exe", "searchhost.exe",
        "startmenuexperiencehost.exe", "shellexperiencehost.exe", "applicationframehost.exe",
        "lockapp.exe", "textinputhost.exe", "sihost.exe", "ctfmon.exe",
        "musync.exe",
        // 音乐播放器（走音乐同步逻辑，不参与程序同步）
        "cloudmusic.exe", "qqmusic.exe", "lx-music-desktop.exe"
    };

    /// <summary>常见程序字典：exe -> (分类, 建议显示名)。</summary>
    private static readonly Dictionary<string, (AppCategory Category, string Name)> KnownPrograms =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // 游戏（非 Steam 启动 / 国服客户端为主；Steam 游戏由 Steam 客户端自行上报）
            ["yuanshen.exe"] = (AppCategory.Game, "原神"),
            ["genshinimpact.exe"] = (AppCategory.Game, "原神"),
            ["starrail.exe"] = (AppCategory.Game, "崩坏：星穹铁道"),
            ["dnf.exe"] = (AppCategory.Game, "地下城与勇士"),
            ["leagueclient.exe"] = (AppCategory.Game, "英雄联盟"),
            ["cs2.exe"] = (AppCategory.Game, "Counter-Strike 2"),
            ["minecraft.exe"] = (AppCategory.Game, "Minecraft"),
            ["hmcl.exe"] = (AppCategory.Game, "Minecraft"),
            ["pcl.exe"] = (AppCategory.Game, "Minecraft"),
            ["strinova.exe"] = (AppCategory.Game, "卡拉彼丘"),
            // 工作
            ["code.exe"] = (AppCategory.Work, "VS Code"),
            ["devenv.exe"] = (AppCategory.Work, "Visual Studio"),
            ["pycharm64.exe"] = (AppCategory.Work, "PyCharm"),
            ["idea64.exe"] = (AppCategory.Work, "IntelliJ IDEA"),
            ["webstorm64.exe"] = (AppCategory.Work, "WebStorm"),
            ["goland64.exe"] = (AppCategory.Work, "GoLand"),
            ["notepad++.exe"] = (AppCategory.Work, "Notepad++"),
            ["sublime_text.exe"] = (AppCategory.Work, "Sublime Text"),
            ["winword.exe"] = (AppCategory.Work, "Word"),
            ["excel.exe"] = (AppCategory.Work, "Excel"),
            ["powerpnt.exe"] = (AppCategory.Work, "PowerPoint"),
            ["wps.exe"] = (AppCategory.Work, "WPS Office"),
            ["et.exe"] = (AppCategory.Work, "WPS 表格"),
            ["blender.exe"] = (AppCategory.Work, "Blender"),
            ["photoshop.exe"] = (AppCategory.Work, "Photoshop"),
            ["cmd.exe"] = (AppCategory.Work, "命令行"),
            ["windowsterminal.exe"] = (AppCategory.Work, "Windows Terminal"),
            ["powershell.exe"] = (AppCategory.Work, "PowerShell"),
            ["wezterm-gui.exe"] = (AppCategory.Work, "WezTerm"),
            ["mintty.exe"] = (AppCategory.Work, "Git Bash"),
            // 媒体
            ["potplayermini64.exe"] = (AppCategory.Media, "PotPlayer"),
            ["potplayermini.exe"] = (AppCategory.Media, "PotPlayer"),
            ["mpv.exe"] = (AppCategory.Media, "mpv"),
            ["vlc.exe"] = (AppCategory.Media, "VLC"),
            ["obs64.exe"] = (AppCategory.Media, "OBS Studio"),
            ["bdcam.exe"] = (AppCategory.Media, "Bandicam"),
            // 社交
            ["qq.exe"] = (AppCategory.Social, "QQ"),
            ["wechat.exe"] = (AppCategory.Social, "微信"),
            ["weixin.exe"] = (AppCategory.Social, "微信"),
            ["wxwork.exe"] = (AppCategory.Social, "企业微信"),
            ["telegram.exe"] = (AppCategory.Social, "Telegram"),
            ["discord.exe"] = (AppCategory.Social, "Discord"),
            ["dingtalk.exe"] = (AppCategory.Social, "钉钉"),
            ["feishu.exe"] = (AppCategory.Social, "飞书"),
            // 浏览器 / 其他
            ["chrome.exe"] = (AppCategory.Other, "Chrome"),
            ["msedge.exe"] = (AppCategory.Other, "Edge"),
            ["firefox.exe"] = (AppCategory.Other, "Firefox"),
            ["steam.exe"] = (AppCategory.Other, "Steam")
        };

    public static bool IsBuiltInIgnored(string exeName) => BuiltInIgnored.Contains(exeName);

    /// <summary>
    /// 给新发现的程序一个分类与显示名建议。
    /// FromDictionary=true 表示命中了内置字典（置信度较高）。
    /// </summary>
    public static (AppCategory Category, string SuggestedName, bool FromDictionary) Suggest(
        string exeName, string windowTitle, bool isFullscreen)
    {
        if (KnownPrograms.TryGetValue(exeName, out var known))
        {
            return (known.Category, known.Name, true);
        }
        var lower = exeName.ToLowerInvariant();
        if (lower.Contains("game") || lower.Contains("launcher") || lower.Contains("unity") ||
            lower.Contains("unreal") || lower.Contains("play"))
        {
            return (AppCategory.Game, DefaultName(exeName, windowTitle), false);
        }
        if (isFullscreen)
        {
            return (AppCategory.Game, DefaultName(exeName, windowTitle), false);
        }
        if (lower.Contains("code") || lower.Contains("studio") || lower.Contains("edit") ||
            lower.Contains("term"))
        {
            return (AppCategory.Work, DefaultName(exeName, windowTitle), false);
        }
        if (lower.Contains("player") || lower.Contains("music") || lower.Contains("video"))
        {
            return (AppCategory.Media, DefaultName(exeName, windowTitle), false);
        }
        return (AppCategory.Other, DefaultName(exeName, windowTitle), false);
    }

    private static string DefaultName(string exeName, string windowTitle)
    {
        if (!string.IsNullOrWhiteSpace(windowTitle))
        {
            var title = windowTitle.Trim();
            // 窗口标题常形如 "文档 - 程序"，过长时截取（用户可在设置里改）
            if (title.Length > 40)
            {
                title = title[..40];
            }
            return title;
        }
        return Path.GetFileNameWithoutExtension(exeName);
    }
}
