using System;
using System.Collections.Generic;
using MuSync.Models;
using MuSync.Utils;

namespace MuSync;

internal sealed partial class SettingsView
{
    // Reuse native text fields/spinners when returning to settings; still restore unsaved inputs verbatim.
    private void ReloadDataSourceRows(IReadOnlyList<DataSourceConfig> sources)
    {
        _dataSourceRowsRevision++;
        _dataSourceProbeRun++;
        var wasTesting = !_dataSourceTestButton.Enabled;
        _dataSourceTestButton.Enabled = true;
        if (wasTesting)
        {
            _dataSourceHint.ForeColor = Theme.Muted;
            _dataSourceHint.Text = Loc.L("配置已重新加载，请重新测试。", "Settings reloaded. Please test again.");
        }
        _dataSourcesPanel.SuspendLayout();
        try
        {
            while (_dataSourceRows.Count > sources.Count)
            {
                var last = _dataSourceRows[^1];
                _dataSourceRows.RemoveAt(_dataSourceRows.Count - 1);
                _dataSourcesPanel.Controls.Remove(last.Container);
                last.Container.Dispose();
            }
            for (var i = 0; i < sources.Count; i++)
            {
                var source = sources[i];
                if (i >= _dataSourceRows.Count) { AddDataSourceRow(source); continue; }
                var row = _dataSourceRows[i];
                var changed = row.Enabled.Checked != source.Enabled || row.Name.Text != source.Name ||
                    row.Url.Text != source.Url || row.ValuePath.Text != source.ValuePath || row.Format.Text != source.Format ||
                    row.Refresh.Value != Math.Clamp(source.RefreshSeconds, 1, 3600);
                row.Enabled.Checked = source.Enabled;
                row.Name.Text = source.Name;
                row.Url.Text = source.Url;
                row.ValuePath.Text = source.ValuePath;
                row.Format.Text = source.Format;
                row.Refresh.Value = Math.Clamp(source.RefreshSeconds, 1, 3600);
                if (changed)
                {
                    row.Status.Text = Loc.L("状态：未测试", "Status: not tested");
                    row.Status.ForeColor = Theme.Muted;
                }
            }
            RelayoutDataSourceRows();
        }
        finally { _dataSourcesPanel.ResumeLayout(true); }
    }

    private sealed class AppRuleValueComparer : IEqualityComparer<AppRule>
    {
        internal static readonly AppRuleValueComparer Instance = new();
        public bool Equals(AppRule? a, AppRule? b) => ReferenceEquals(a,b) || a is not null && b is not null &&
            a.ExeName == b.ExeName && a.DisplayName == b.DisplayName && a.Category == b.Category &&
            a.Mode == b.Mode && a.Override == b.Override && a.Enabled == b.Enabled && a.IsUserConfirmed == b.IsUserConfirmed;
        public int GetHashCode(AppRule rule) => StringComparer.Ordinal.GetHashCode(rule.ExeName);
    }
}
