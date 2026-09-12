namespace MuSync.Models;

/// <summary>程序分类：决定默认的同步策略。</summary>
internal enum AppCategory
{
    /// <summary>忽略：出现在前台时不产生任何动作（不清状态、不切换）。</summary>
    Ignore,
    /// <summary>游戏：总是参与同步（不受"同步非游戏应用"总开关影响）。</summary>
    Game,
    Work,
    Media,
    Social,
    Other
}

/// <summary>检测模式。</summary>
internal enum AppSyncMode
{
    /// <summary>仅当该程序在前台时显示。</summary>
    Foreground,
    /// <summary>只要进程在运行就显示（后台也算）。</summary>
    Always
}

/// <summary>单程序对分类默认策略的覆盖。</summary>
internal enum AppSyncOverride
{
    FollowCategory,
    ForceOn,
    ForceOff
}

/// <summary>一条程序同步规则（用户配置或自动发现的候选）。</summary>
internal class AppRule
{
    /// <summary>进程可执行文件名，如 "strinova.exe"。</summary>
    public string ExeName { get; set; } = "";
    /// <summary>推送到 Steam 的显示名（留空时使用 ExeName）。</summary>
    public string DisplayName { get; set; } = "";
    public AppCategory Category { get; set; } = AppCategory.Other;
    public AppSyncMode Mode { get; set; } = AppSyncMode.Foreground;
    public AppSyncOverride Override { get; set; } = AppSyncOverride.FollowCategory;
    /// <summary>是否启用该规则（关掉=不检测不显示）。</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>分类是否已被用户确认（false 表示还在"待分类"区，仅使用建议值）。</summary>
    public bool IsUserConfirmed { get; set; }
}
