namespace MuSync.Utils;

/// <summary>界面语言入口（本地化）：内联中英文案，按配置切换；切换后重启生效。
/// 英文文案以仓库 i18n 审校表定稿为准（本项目 2026-09-16 定稿）。</summary>
internal static class Loc
{
    /// <summary>当前是否英文界面（配置 Language：0 = 中文、1 = English）。</summary>
    public static bool IsEnglish => Configurations.Instance.Settings.Language == 1;

    /// <summary>按当前语言取文案：L(中文, English)。</summary>
    public static string L(string zh, string en) => IsEnglish ? en : zh;

    /// <summary>播放器显示名（数据层用中文名，界面按语言映射；兼容 LX Music 两种写法）。</summary>
    public static string PlayerName(string name) => name switch
    {
        "网易云音乐" => L("网易云音乐", "NetEase Cloud Music"),
        "QQ音乐" => L("QQ音乐", "QQ Music"),
        "洛雪音乐" or "LX Music" => L("洛雪音乐", "LX Music"),
        "酷狗音乐" => L("酷狗音乐", "KuGou Music"),
        _ => name
    };
}
