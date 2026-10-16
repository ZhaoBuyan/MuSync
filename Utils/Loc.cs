namespace MuSync.Utils;

/// <summary>界面语言入口（本地化）：内联中英文案，按配置切换；切换后重启生效。</summary>
internal static class Loc
{
    /// <summary>当前是否英文界面（配置 Language：0 = 中文、1 = English）。</summary>
    public static bool IsEnglish => Configurations.Instance.Settings.Language == 1;

    /// <summary>按当前语言取文案：L(中文, English)。</summary>
    public static string L(string zh, string en) => IsEnglish ? en : zh;

    /// <summary>
    /// 播放器显示名（数据层用中文名，界面按语言映射）。
    /// 同时兼容数据层 key（NetEase / Tencent / LxMusic / KuGou / TTPlayer / Browser）与中文名两种写法：
    /// 两套写法都出现过（诊断快照曾用 key 口径），漏认哪个都会让该名字原样漏出去、英文界面下露中文。
    /// </summary>
    public static string PlayerName(string name) => name switch
    {
        "网易云音乐" or "NetEase" => L("网易云音乐", "NetEase Cloud Music"),
        "QQ音乐" or "Tencent" => L("QQ音乐", "QQ Music"),
        "洛雪音乐" or "LX Music" or "LxMusic" => L("洛雪音乐", "LX Music"),
        "酷狗音乐" or "KuGou" => L("酷狗音乐", "KuGou Music"),
        "千千静听" or "TTPlayer" => L("千千静听", "TTPlayer"),
        "浏览器媒体" or "Browser" => L("浏览器媒体", "Browser media"),
        _ => name
    };
}
