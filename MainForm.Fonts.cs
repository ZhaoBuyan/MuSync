using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MuSync.Utils;

namespace MuSync;

internal partial class MainForm
{
    private readonly Dictionary<Control, Font> _defaultFonts = new();
    private readonly List<Font> _appearanceFonts = [];
    private (string Family, float Size, FontStyle Style)? _appliedFontKey;

    private void ApplyAppearanceFont()
    {
        if (_defaultFonts.Count == 0)
        {
            void Remember(Control parent)
            {
                foreach (Control child in parent.Controls)
                {
                    if (child is SettingsView) continue;
                    _defaultFonts.Add(child, child.Font);
                    Remember(child);
                }
            }
            Remember(this);
        }
        var config = Configurations.Instance.Settings;
        var family = config.AppearanceFontFamily?.Trim() ?? "";
        var size = float.IsFinite(config.AppearanceFontSize) && config.AppearanceFontSize > 0
            ? Math.Clamp(config.AppearanceFontSize, 8, 16) : Theme.Body.SizeInPoints;
        var style = (FontStyle)(config.AppearanceFontStyle & 15);
        var custom = family.Length > 0 || config.AppearanceFontSize > 0 || style != FontStyle.Regular;
        var key = (family, custom ? size : 0, style);
        if (_appliedFontKey == key) return;

        var replacements = new Dictionary<(float Size, FontStyle Style), Font>();
        try
        {
            // 先构造完再替换；保留默认标题/正文的相对字号与粗体层级。
            if (custom)
                foreach (var original in _defaultFonts.Values)
                {
                    var role = (original.SizeInPoints * size / Theme.Body.SizeInPoints, original.Style | style);
                    if (!replacements.ContainsKey(role))
                        replacements.Add(role, new Font(family.Length > 0 ? family : Theme.Body.FontFamily.Name,
                            role.Item1, role.Item2, GraphicsUnit.Point));
                }
            foreach (var entry in _defaultFonts)
                if (!entry.Key.IsDisposed)
                    entry.Key.Font = custom
                        ? replacements[(entry.Value.SizeInPoints * size / Theme.Body.SizeInPoints, entry.Value.Style | style)]
                        : entry.Value;
        }
        catch
        {
            _appearanceFonts.AddRange(replacements.Values);
            throw;
        }
        foreach (var font in _appearanceFonts) font.Dispose();
        _appearanceFonts.Clear();
        _appearanceFonts.AddRange(replacements.Values);
        _appliedFontKey = key;
        RelayoutForSize();
    }
}
