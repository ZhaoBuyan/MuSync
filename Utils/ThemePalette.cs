using System.Drawing;
using System.Linq;

namespace MuSync.Utils;

internal sealed record ThemePalette(string Id, string Chinese, string English,
    Color Paper, Color Inset, Color Canvas, Color Ink, Color Muted, Color Rule,
    Color Accent, Color Sidebar, Color SidebarMuted, Color SidebarRule, Color SidebarSelected)
{
    internal bool IsDark => Paper.GetBrightness() < .5f;
    internal Color Surface => IsDark ? Color.FromArgb(Paper.R + 8, Paper.G + 8, Paper.B + 8) : Color.White;
    internal Color Error => ColorTranslator.FromHtml(IsDark ? "#FF9A94" : "#D22B1F");
    internal Color Warning => ColorTranslator.FromHtml(IsDark ? "#E5BB73" : "#A86A12");
    public override string ToString() => Loc.L(Chinese, English);
}

internal static class ThemePalettes
{
    private static Color C(string hex) => ColorTranslator.FromHtml(hex);
    internal static readonly ThemePalette[] All =
    [
        new("fog", "雾白青", "Fog & teal", C("#FAFBFC"), C("#E7EEF1"), C("#EEF2F4"), C("#23313B"), C("#61727D"), C("#CAD5DC"), C("#256F78"), C("#20323E"), C("#A8BAC5"), C("#506571"), C("#304D5B")),
        new("sage", "鼠尾草绿", "Sage garden", C("#F7F9F4"), C("#E7EDE1"), C("#EEF2E9"), C("#28382E"), C("#637363"), C("#CBD5C5"), C("#426B50"), C("#263D32"), C("#B4C8B5"), C("#5C7864"), C("#3C5845")),
        new("lavender", "雾紫", "Lavender mist", C("#FAF8FD"), C("#EDE7F5"), C("#F1EDF7"), C("#352D43"), C("#746780"), C("#D7CCE1"), C("#705592"), C("#352D46"), C("#C7BBD7"), C("#78658E"), C("#514164")),
        new("clay", "暖陶", "Warm terracotta", C("#FCF8F4"), C("#F2E7DD"), C("#F6EEE7"), C("#3D302A"), C("#7E6A60"), C("#DECFC3"), C("#98553F"), C("#42342E"), C("#D1BBAC"), C("#846D5D"), C("#624A3D"))
    ];
    // 暂停开放：仅保留原型用于本地排查，不进入可选列表或配置解析路径。
    internal static readonly ThemePalette NightPrototype = new("night", "静夜", "Quiet night", C("#171B20"), C("#2B333D"), C("#1D232B"), C("#E5EAF0"), C("#ABB6C4"), C("#526071"), C("#9EBED9"), C("#11161C"), C("#AFBECD"), C("#526276"), C("#293B4D"));
    internal static ThemePalette Resolve(string? id) => All.FirstOrDefault(p => p.Id == id) ?? All[0];
}
