using System;
using System.Windows.Forms;

namespace MuSync.Utils;

/// <summary>批量布局期间保持宿主窗口可见；不改动 HWND 的 WS_VISIBLE 状态。</summary>
internal sealed class WindowUpdateScope : IDisposable
{
    private readonly Control _host;
    private readonly bool _repaintChildren;

    internal WindowUpdateScope(Control host, bool repaintChildren = true)
    {
        _host = host;
        _repaintChildren = repaintChildren;
        host.SuspendLayout();
    }

    public void Dispose()
    {
        if (!_host.IsDisposed)
        {
            _host.ResumeLayout(true);
            _host.Invalidate(_repaintChildren);
        }
    }

}
