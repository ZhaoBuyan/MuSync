using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        // 系统外壳 / 输入法 / 开始菜单 / 浏览器与 Steam 的后台子进程：
        // 它们经常在前台或随主程序常驻，若不同步忽略，"正在使用"会显示成它们而不是用户真正在用的程序
        "lockapp.exe", "textinputhost.exe", "sihost.exe", "ctfmon.exe",
        "searchapp.exe", "msedgewebview2.exe", "steamwebhelper.exe",
        "musync.exe",
        // 音乐播放器（走音乐同步逻辑，不参与程序同步）
        "cloudmusic.exe", "qqmusic.exe", "lx-music-desktop.exe",
        "kugou.exe", "ttplayer.exe", "ttplayerrebuild.exe",
        // 系统 / 驱动 / 安装程序 / 安全软件：误当成"正在玩的程序"没有意义
        "mmc.exe", "dllhost.exe", "rundll32.exe", "regsvr32.exe", "conhost.exe",
        "msiexec.exe", "setup.exe", "install.exe", "uninstall.exe", "unins000.exe", "update.exe",
        "widgets.exe", "windowssecurity.exe", "securityhealthsystray.exe",
        "360tray.exe", "360se.exe", "huorong.exe", "hipstray.exe",
        "nvcontainer.exe", "rtss.exe", "msiafterburner.exe", "fraps.exe",
        // 游戏反作弊 / 运行时辅助进程：它们会随游戏一起出现、还常抢到前台，
        // 若不忽略，"正在玩"会显示成反作弊而不是游戏本身。
        // ACE = 腾讯反作弊（主进程 SGuard 系列、辅助进程 ACE-Helper）；
        // aces / aces_BE 是 BattlEye 的启动器 —— 都在实机进程里见过抢前台
        "anticheatexpert.exe", "acegame.exe", "ace-helper.exe",
        "sguard.exe", "sguard64.exe", "sguardsvc.exe", "sguardsvc64.exe",
        "aces.exe", "aces_be.exe", "belauncher.exe",
        "mhyprot3.exe", "easyanticheat.exe", "easyanticheat_eos.exe", "beservice.exe", "bedaisy.exe",
        "vgc.exe", "vgtray.exe", "riotclientservices.exe"
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
            // 启动器用的是通用名（含空格），必须精确匹配，不能靠关键词规则
            ["plain craft launcher 2.exe"] = (AppCategory.Game, "Plain Craft Launcher"),
            ["strinova.exe"] = (AppCategory.Game, "卡拉彼丘"),
            // 卡拉彼丘的 UE 打包名（与 strinova.exe 同一游戏）
            ["calabiyau-win64-shipping.exe"] = (AppCategory.Game, "卡拉彼丘"),
            ["naraka.exe"] = (AppCategory.Game, "永劫无间"),
            ["deltarune.exe"] = (AppCategory.Game, "Deltarune"),
            ["bluearchive.exe"] = (AppCategory.Game, "蔚蓝档案"),
            ["wutheringwaves.exe"] = (AppCategory.Game, "鸣潮"),
            ["client-win64-shipping.exe"] = (AppCategory.Game, "鸣潮"),
            ["toweroffantasy.exe"] = (AppCategory.Game, "幻塔"),
            ["arknights.exe"] = (AppCategory.Game, "明日方舟"),
            ["arknightsendfield.exe"] = (AppCategory.Game, "明日方舟：终末地"),
            ["endfield.exe"] = (AppCategory.Game, "明日方舟：终末地"),
            ["pubg.exe"] = (AppCategory.Game, "PUBG"),
            ["tslgame.exe"] = (AppCategory.Game, "PUBG"),
            ["overwatch.exe"] = (AppCategory.Game, "守望先锋"),
            ["r5apex.exe"] = (AppCategory.Game, "Apex 英雄"),
            ["dota2.exe"] = (AppCategory.Game, "Dota 2"),
            ["gta5.exe"] = (AppCategory.Game, "GTA V"),
            ["rdr2.exe"] = (AppCategory.Game, "荒野大镖客 2"),
            ["eldenring.exe"] = (AppCategory.Game, "艾尔登法环"),
            ["cyberpunk2077.exe"] = (AppCategory.Game, "赛博朋克 2077"),
            ["bg3.exe"] = (AppCategory.Game, "博德之门 3"),
            ["bg3_dx11.exe"] = (AppCategory.Game, "博德之门 3"),
            // 模拟器
            ["dolphin.exe"] = (AppCategory.Game, "Dolphin 模拟器"),
            ["rpcs3.exe"] = (AppCategory.Game, "RPCS3"),
            ["yuzu.exe"] = (AppCategory.Game, "Yuzu"),
            ["citron.exe"] = (AppCategory.Game, "Citron"),
            ["ryujinx.exe"] = (AppCategory.Game, "Ryujinx"),
            ["cemu.exe"] = (AppCategory.Game, "Cemu"),
            ["ppssppwindows64.exe"] = (AppCategory.Game, "PPSSPP"),
            ["mumunxmain.exe"] = (AppCategory.Game, "MuMu 模拟器"),
            ["ldplayer.exe"] = (AppCategory.Game, "雷电模拟器"),
            ["dnplayer.exe"] = (AppCategory.Game, "雷电模拟器"),
            ["bluestacks.exe"] = (AppCategory.Game, "BlueStacks"),
            ["hd-player.exe"] = (AppCategory.Game, "BlueStacks"),
            // 单机 / 独立游戏与国产客户端（进程名以安装目录实测为准）
            ["calabiyau.exe"] = (AppCategory.Game, "卡拉彼丘"),
            ["osu!.exe"] = (AppCategory.Game, "osu!"),
            ["musedash.exe"] = (AppCategory.Game, "Muse Dash"),
            ["balatro.exe"] = (AppCategory.Game, "小丑牌"),
            ["slaythespire.exe"] = (AppCategory.Game, "杀戮尖塔"),
            ["vividstasis.exe"] = (AppCategory.Game, "vivid/stasis"),
            ["misidefull.exe"] = (AppCategory.Game, "米塔 MiSide"),
            ["clannad.exe"] = (AppCategory.Game, "CLANNAD"),
            ["lobotomycorp.exe"] = (AppCategory.Game, "脑叶公司"),
            ["unreal_life.exe"] = (AppCategory.Game, "非现实生活"),
            ["chill with you.exe"] = (AppCategory.Game, "放松时光"),
            ["lostcastle2.exe"] = (AppCategory.Game, "失落城堡 2"),
            ["iron nest heavy turret simulator.exe"] = (AppCategory.Game, "铁巢重炮"),
            ["stardew valley.exe"] = (AppCategory.Game, "星露谷物语"),
            ["terraria.exe"] = (AppCategory.Game, "泰拉瑞亚"),
            ["hollow_knight.exe"] = (AppCategory.Game, "空洞骑士"),
            ["isaac.exe"] = (AppCategory.Game, "以撒的结合"),
            ["hades.exe"] = (AppCategory.Game, "哈迪斯"),
            ["cities.exe"] = (AppCategory.Game, "城市：天际线"),
            ["cities2.exe"] = (AppCategory.Game, "城市：天际线 II"),
            ["stellaris.exe"] = (AppCategory.Game, "群星"),
            ["victoria3.exe"] = (AppCategory.Game, "维多利亚 3"),
            ["eu4.exe"] = (AppCategory.Game, "欧陆风云 IV"),
            ["ck3.exe"] = (AppCategory.Game, "十字军之王 III"),
            ["peak.exe"] = (AppCategory.Game, "PEAK"),
            ["vrchat.exe"] = (AppCategory.Game, "VRChat"),
            ["bh3.exe"] = (AppCategory.Game, "崩坏 3"),
            ["hyp.exe"] = (AppCategory.Game, "绝区零"),
            ["zenlesszonezero.exe"] = (AppCategory.Game, "绝区零"),
            ["ntecloudlauncher.exe"] = (AppCategory.Game, "云异环"),
            // 工作
            ["code.exe"] = (AppCategory.Work, "VS Code"),
            ["devenv.exe"] = (AppCategory.Work, "Visual Studio"),
            ["pycharm64.exe"] = (AppCategory.Work, "PyCharm"),
            ["idea64.exe"] = (AppCategory.Work, "IntelliJ IDEA"),
            ["webstorm64.exe"] = (AppCategory.Work, "WebStorm"),
            ["goland64.exe"] = (AppCategory.Work, "GoLand"),
            ["clion64.exe"] = (AppCategory.Work, "CLion"),
            ["rider64.exe"] = (AppCategory.Work, "Rider"),
            ["datagrip64.exe"] = (AppCategory.Work, "DataGrip"),
            ["androidstudio64.exe"] = (AppCategory.Work, "Android Studio"),
            ["studio64.exe"] = (AppCategory.Work, "Android Studio"),
            ["eclipse.exe"] = (AppCategory.Work, "Eclipse"),
            ["netbeans64.exe"] = (AppCategory.Work, "NetBeans"),
            ["notepad++.exe"] = (AppCategory.Work, "Notepad++"),
            ["sublime_text.exe"] = (AppCategory.Work, "Sublime Text"),
            ["winword.exe"] = (AppCategory.Work, "Word"),
            ["excel.exe"] = (AppCategory.Work, "Excel"),
            ["powerpnt.exe"] = (AppCategory.Work, "PowerPoint"),
            ["onenote.exe"] = (AppCategory.Work, "OneNote"),
            ["outlook.exe"] = (AppCategory.Work, "Outlook"),
            ["msaccess.exe"] = (AppCategory.Work, "Access"),
            ["wps.exe"] = (AppCategory.Work, "WPS Office"),
            ["et.exe"] = (AppCategory.Work, "WPS 表格"),
            ["wpp.exe"] = (AppCategory.Work, "WPS 演示"),
            ["blender.exe"] = (AppCategory.Work, "Blender"),
            ["photoshop.exe"] = (AppCategory.Work, "Photoshop"),
            ["illustrator.exe"] = (AppCategory.Work, "Illustrator"),
            ["premiere pro.exe"] = (AppCategory.Work, "Premiere Pro"),
            ["afterfx.exe"] = (AppCategory.Work, "After Effects"),
            ["figma.exe"] = (AppCategory.Work, "Figma"),
            ["xd.exe"] = (AppCategory.Work, "Adobe XD"),
            ["axure.exe"] = (AppCategory.Work, "Axure"),
            ["mspaint.exe"] = (AppCategory.Work, "画图"),
            ["snippingtool.exe"] = (AppCategory.Work, "截图工具"),
            ["snipaste.exe"] = (AppCategory.Work, "Snipaste"),
            ["typora.exe"] = (AppCategory.Work, "Typora"),
            ["obsidian.exe"] = (AppCategory.Work, "Obsidian"),
            ["notion.exe"] = (AppCategory.Work, "Notion"),
            ["xmind.exe"] = (AppCategory.Work, "XMind"),
            ["navicat.exe"] = (AppCategory.Work, "Navicat"),
            ["dbeaver.exe"] = (AppCategory.Work, "DBeaver"),
            ["heidisql.exe"] = (AppCategory.Work, "HeidiSQL"),
            ["git-bash.exe"] = (AppCategory.Work, "Git Bash"),
            ["cmd.exe"] = (AppCategory.Work, "命令行"),
            ["windowsterminal.exe"] = (AppCategory.Work, "Windows Terminal"),
            ["powershell.exe"] = (AppCategory.Work, "PowerShell"),
            ["pwsh.exe"] = (AppCategory.Work, "PowerShell"),
            ["wezterm-gui.exe"] = (AppCategory.Work, "WezTerm"),
            ["mintty.exe"] = (AppCategory.Work, "Git Bash"),
            ["alacritty.exe"] = (AppCategory.Work, "Alacritty"),
            ["mobaxterm.exe"] = (AppCategory.Work, "MobaXterm"),
            ["xshell.exe"] = (AppCategory.Work, "Xshell"),
            ["putty.exe"] = (AppCategory.Work, "PuTTY"),
            ["winscp.exe"] = (AppCategory.Work, "WinSCP"),
            ["filezilla.exe"] = (AppCategory.Work, "FileZilla"),
            ["7zfm.exe"] = (AppCategory.Work, "7-Zip"),
            ["winrar.exe"] = (AppCategory.Work, "WinRAR"),
            ["bandizip.exe"] = (AppCategory.Work, "Bandizip"),
            ["kicad.exe"] = (AppCategory.Work, "KiCad"),
            ["eeschema.exe"] = (AppCategory.Work, "KiCad 原理图"),
            ["pcbnew.exe"] = (AppCategory.Work, "KiCad PCB"),
            ["gerbview.exe"] = (AppCategory.Work, "KiCad Gerber 查看器"),
            ["pcb_calculator.exe"] = (AppCategory.Work, "KiCad 计算器"),
            ["ltspice.exe"] = (AppCategory.Work, "LTspice"),
            ["uv4.exe"] = (AppCategory.Work, "Keil uVision"),
            ["stm32cubemx.exe"] = (AppCategory.Work, "STM32CubeMX"),
            ["lceda-pro.exe"] = (AppCategory.Work, "嘉立创EDA"),
            ["fl64.exe"] = (AppCategory.Work, "FL Studio"),
            ["krita.exe"] = (AppCategory.Work, "Krita"),
            ["unity.exe"] = (AppCategory.Work, "Unity"),
            ["unity hub.exe"] = (AppCategory.Work, "Unity Hub"),
            ["creatorcompanion.exe"] = (AppCategory.Work, "VRChat Creator Companion"),
            ["ea.exe"] = (AppCategory.Work, "Enterprise Architect"),
            ["vmware.exe"] = (AppCategory.Work, "VMware Workstation"),
            ["vmplayer.exe"] = (AppCategory.Work, "VMware Player"),
            ["docker desktop.exe"] = (AppCategory.Work, "Docker Desktop"),
            ["node.exe"] = (AppCategory.Work, "Node.js"),
            ["python.exe"] = (AppCategory.Work, "Python"),
            ["pythonw.exe"] = (AppCategory.Work, "Python"),
            ["mysql.exe"] = (AppCategory.Work, "MySQL"),
            ["mysqlworkbench.exe"] = (AppCategory.Work, "MySQL Workbench"),
            ["mysqlinstaller.exe"] = (AppCategory.Work, "MySQL Installer"),
            ["ssms.exe"] = (AppCategory.Work, "SQL Server Management Studio"),
            ["dtswizard.exe"] = (AppCategory.Work, "SQL Server 导入导出"),
            ["clash-verge.exe"] = (AppCategory.Work, "Clash Verge"),
            ["dev-sidecar.exe"] = (AppCategory.Work, "dev-sidecar"),
            ["chatbox.exe"] = (AppCategory.Work, "Chatbox"),
            ["jianyingpro.exe"] = (AppCategory.Work, "剪映专业版"),
            ["bcut.exe"] = (AppCategory.Work, "必剪"),
            ["iflyrecclient.exe"] = (AppCategory.Work, "讯飞听见"),
            ["jane reader.exe"] = (AppCategory.Work, "Jane Reader"),
            ["yuewenedit.exe"] = (AppCategory.Work, "作家助手"),
            ["bitmap2component.exe"] = (AppCategory.Work, "KiCad 图片转换器"),
            // 媒体
            ["potplayermini64.exe"] = (AppCategory.Media, "PotPlayer"),
            ["potplayermini.exe"] = (AppCategory.Media, "PotPlayer"),
            ["potplayer.exe"] = (AppCategory.Media, "PotPlayer"),
            ["mpv.exe"] = (AppCategory.Media, "mpv"),
            ["vlc.exe"] = (AppCategory.Media, "VLC"),
            ["obs64.exe"] = (AppCategory.Media, "OBS Studio"),
            ["obs32.exe"] = (AppCategory.Media, "OBS Studio"),
            ["bdcam.exe"] = (AppCategory.Media, "Bandicam"),
            ["screenrecord.exe"] = (AppCategory.Media, "傲软录屏"),
            ["cap.exe"] = (AppCategory.Media, "Cap 录屏"),
            // 社交
            ["qq.exe"] = (AppCategory.Social, "QQ"),
            ["wechat.exe"] = (AppCategory.Social, "微信"),
            ["weixin.exe"] = (AppCategory.Social, "微信"),
            ["wxwork.exe"] = (AppCategory.Social, "企业微信"),
            ["telegram.exe"] = (AppCategory.Social, "Telegram"),
            ["discord.exe"] = (AppCategory.Social, "Discord"),
            ["dingtalk.exe"] = (AppCategory.Social, "钉钉"),
            ["feishu.exe"] = (AppCategory.Social, "飞书"),
            ["lark.exe"] = (AppCategory.Social, "飞书"),
            ["zoom.exe"] = (AppCategory.Social, "Zoom"),
            ["slack.exe"] = (AppCategory.Social, "Slack"),
            ["teams.exe"] = (AppCategory.Social, "Microsoft Teams"),
            ["ms-teams.exe"] = (AppCategory.Social, "Microsoft Teams"),
            ["skype.exe"] = (AppCategory.Social, "Skype"),
            ["kook.exe"] = (AppCategory.Social, "KOOK"),
            // 语音 / 聊天客户端
            ["oopz.exe"] = (AppCategory.Social, "Oopz"),
            ["yylite.exe"] = (AppCategory.Social, "YY"),
            ["yy.exe"] = (AppCategory.Social, "YY"),
            // 浏览器 / 其他
            ["chrome.exe"] = (AppCategory.Other, "Chrome"),
            ["msedge.exe"] = (AppCategory.Other, "Edge"),
            ["firefox.exe"] = (AppCategory.Other, "Firefox"),
            ["brave.exe"] = (AppCategory.Other, "Brave"),
            ["opera.exe"] = (AppCategory.Other, "Opera"),
            ["iexplore.exe"] = (AppCategory.Other, "Internet Explorer"),
            ["quark.exe"] = (AppCategory.Other, "夸克"),
            ["360chromex.exe"] = (AppCategory.Other, "360 极速浏览器"),
            ["uu_launcher.exe"] = (AppCategory.Other, "UU 加速器"),
            // 加速器主程序（与 uu_launcher.exe 同一软件）
            ["uu.exe"] = (AppCategory.Other, "UU 加速器"),
            ["heyboxacc.exe"] = (AppCategory.Other, "小黑盒加速器"),
            ["sakuralauncher.exe"] = (AppCategory.Other, "SakuraFrp"),
            ["i4launcher.exe"] = (AppCategory.Other, "爱思助手"),
            ["honorsuite.exe"] = (AppCategory.Other, "荣耀手机助理"),
            ["lenovopcmanager.exe"] = (AppCategory.Other, "联想电脑管家"),
            ["powertoys.exe"] = (AppCategory.Other, "PowerToys"),
            ["hibituninstaller.exe"] = (AppCategory.Other, "HiBit Uninstaller"),
            ["hibitstartupmanager.exe"] = (AppCategory.Other, "HiBit 启动管理器"),
            ["hibitsysinfo.exe"] = (AppCategory.Other, "HiBit 系统信息"),
            ["privazer.exe"] = (AppCategory.Other, "PrivaZer"),
            ["gamepp.exe"] = (AppCategory.Other, "游戏加加"),
            ["perfectworldarena.exe"] = (AppCategory.Other, "完美世界竞技平台"),
            ["完美世界竞技平台.exe"] = (AppCategory.Other, "完美世界竞技平台"),
            ["vrmonitor.exe"] = (AppCategory.Other, "SteamVR"),
            ["vtuberstudio.exe"] = (AppCategory.Other, "VTube Studio"),
            ["gaomonTablet.exe"] = (AppCategory.Other, "Gaomon 数位板"),
            ["acer mousehub.exe"] = (AppCategory.Other, "Acer MouseHub"),
            ["agents anywhere.exe"] = (AppCategory.Other, "Agents Anywhere"),
            ["steam.exe"] = (AppCategory.Other, "Steam"),
            ["epicgameslauncher.exe"] = (AppCategory.Other, "Epic Games Launcher"),
            ["wegame.exe"] = (AppCategory.Other, "WeGame"),
            ["battle.net.exe"] = (AppCategory.Other, "战网"),
            ["ubisoftconnect.exe"] = (AppCategory.Other, "Ubisoft Connect"),
            ["upc.exe"] = (AppCategory.Other, "Ubisoft Connect"),
            ["origin.exe"] = (AppCategory.Other, "EA App"),
            ["eadesktop.exe"] = (AppCategory.Other, "EA App"),
            ["goggalaxy.exe"] = (AppCategory.Other, "GOG Galaxy")
        };

    public static bool IsBuiltInIgnored(string exeName) => BuiltInIgnored.Contains(exeName);

    /// <summary>
    /// 程序名库规模（内置字典 + 内置忽略名单）：设置页「程序」页展示用，
    /// 让用户一眼看出分类建议来自本机库、不需要联网。
    /// </summary>
    public static int LibrarySize => KnownPrograms.Count + BuiltInIgnored.Count;

    /// <summary>
    /// 给新发现的程序一个分类与显示名建议。
    /// FromDictionary=true 表示命中了内置字典（置信度较高）。
    /// <para>
    /// <paramref name="exePath"/> 可选：给了就优先从可执行文件的版本信息取产品名，
    /// 免得程序列表里出现一堆 "xxx.exe" 这种没人看得懂的名字。
    /// </para>
    /// </summary>
    public static (AppCategory Category, string SuggestedName, bool FromDictionary) Suggest(
        string exeName, string windowTitle, bool isFullscreen, string? exePath = null)
    {
        if (KnownPrograms.TryGetValue(exeName, out var known))
        {
            return (known.Category, known.Name, true);
        }
        var suggestedName = SuggestedNameOf(exeName, windowTitle, exePath);
        var lower = exeName.ToLowerInvariant();
        if (lower.Contains("game") || lower.Contains("launcher") || lower.Contains("unity") ||
            lower.Contains("unreal") || lower.Contains("play"))
        {
            return (AppCategory.Game, suggestedName, false);
        }
        if (isFullscreen)
        {
            return (AppCategory.Game, suggestedName, false);
        }
        if (lower.Contains("code") || lower.Contains("studio") || lower.Contains("edit") ||
            lower.Contains("term"))
        {
            return (AppCategory.Work, suggestedName, false);
        }
        if (lower.Contains("player") || lower.Contains("music") || lower.Contains("video"))
        {
            return (AppCategory.Media, suggestedName, false);
        }
        return (AppCategory.Other, suggestedName, false);
    }

    /// <summary>
    /// 未收录程序的显示名：优先取可执行文件版本信息里的产品名，其次窗口标题，
    /// 都没有才退回文件名（去掉 .exe）。
    /// <para>
    /// 为什么标题放在第二档：标题常带当前文档名（"文档 - 程序"），不如产品名稳定；
    /// 但版本信息并不总有（自解压包、部分国产绿色软件），所以仍要留作兜底。
    /// </para>
    /// </summary>
    public static string SuggestedNameOf(string exeName, string windowTitle, string? exePath = null)
    {
        var productName = ReadProductName(exePath);
        if (productName.Length > 0) return productName;
        if (!string.IsNullOrWhiteSpace(windowTitle)) return Truncate(windowTitle.Trim());
        return Path.GetFileNameWithoutExtension(exeName);
    }

    /// <summary>
    /// 从可执行文件的版本信息里取程序名（ProductName 优于 FileDescription）。
    /// 任何失败（路径为空、文件被删、权限不足、没有版本资源）都返回空串，由调用方退回下一档。
    /// 只读取文件的版本资源，不执行、不联网。
    /// </summary>
    public static string ReadProductName(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return "";
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            var name = PickUsefulName(info.ProductName, info.FileDescription);
            return name.Length > 0 ? Truncate(name) : "";
        }
        catch (Exception ex)
        {
            // 读不到版本信息是常态（绿色软件、自解压包），只记调试输出，不进日志文件
            Debug.WriteLine($"[AppClassifier] 读取版本信息失败({exePath}): {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// 在候选名里挑一个能用的：跳过空串、资源占位符（形如 "@%1"、"%1"）与
    /// 微软系程序常见的泛化产品名（"Microsoft® Windows® Operating System" 之类，
    /// 拿来当"正在玩"的名字毫无信息量，不如退回窗口标题）。
    /// </summary>
    internal static string PickUsefulName(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var name = candidate?.Trim() ?? "";
            if (name.Length == 0) continue;
            if (name.Contains('%') || name.StartsWith('@')) continue;
            if (IsGenericName(name)) continue;
            return name;
        }
        return "";
    }

    /// <summary>泛化到没有信息量的产品名：这类名字下退回窗口标题或文件名反而更好认。</summary>
    private static bool IsGenericName(string name)
    {
        // 去掉 ® ™ 与空白后比较，免得 "Microsoft® Windows®" 这类因符号漏网
        var normalized = name.Replace("®", "").Replace("™", "").Replace(" ", "").Trim();
        foreach (var generic in GenericProductNames)
        {
            if (normalized.Equals(generic, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static readonly string[] GenericProductNames =
    [
        "MicrosoftWindowsOperatingSystem",
        "WindowsOperatingSystem",
        "MicrosoftWindows",
        "Windows",
        "MicrosoftCorporation",
        "Microsoft",
        "Application",
        "App",
        "Setup",
        "Installer",
        "Java(TM)PlatformSEbinary",
        "JavaPlatformSEbinary"
    ];

    /// <summary>过长时截取（用户可在设置里改）。</summary>
    private static string Truncate(string value) => value.Length > 40 ? value[..40] : value;
}
