using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MuSync.Utils;

/// <summary>主题绘制；深色鼠标交互不进入 Windows 会直接绘制浅色滚动条的追踪循环。</summary>
internal sealed partial class ThemeScrollbars : NativeWindow
{
    private static readonly ConditionalWeakTable<Control, ThemeScrollbars> Hooks = new();
    private readonly Control _owner;

    internal static void Attach(Control owner) => _ = Hooks.GetValue(owner, c => new ThemeScrollbars(c));

    private ThemeScrollbars(Control owner)
    {
        _owner = owner;
        owner.HandleCreated += (_, _) => AssignHandle(owner.Handle);
        owner.HandleDestroyed += (_, _) => { EndTracking(); ReleaseHandle(); };
        owner.VisibleChanged += (_, _) => { if (!owner.Visible) EndTracking(); };
        if (owner.Parent is Control parent)
            parent.VisibleChanged += (_, _) => { if (!parent.Visible) EndTracking(); };
        owner.Disposed += (_, _) => { EndTracking(); _repeatTimer?.Dispose(); };
        owner.BackColorChanged += (_, _) =>
        {
            if (!Theme.Palette.IsDark) EndTracking();
            if (owner.IsHandleCreated) RedrawWindow(owner.Handle, IntPtr.Zero, IntPtr.Zero, 0x401);
        };
        if (owner.IsHandleCreated) AssignHandle(owner.Handle);
    }

    protected override void WndProc(ref Message m)
    {
        if (HandleMouse(ref m)) { m.Result = IntPtr.Zero; return; }
        if (Theme.Palette.IsDark && _owner is ScrollBar)
        {
            // 不先画原生浅色再覆盖；WM_PAINT 必须验证更新区，否则会持续收到重绘消息。
            if (m.Msg == 0xF)
            {
                BeginPaint(Handle, out var paint);
                try { PaintBars(IntPtr.Zero); }
                finally { EndPaint(Handle, ref paint); }
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg is 0x317 or 0x318) { PaintBars(m.WParam); m.Result = IntPtr.Zero; return; }
            if (m.Msg == 0x14) { m.Result = (IntPtr)1; return; }
            // 原生鼠标热态也会直接绘制，主题滚动条不使用系统热态。
            if (m.Msg is 0x200 or 0x2A3) { m.Result = IntPtr.Zero; return; }
            if (m.Msg == 0xE0) m.LParam = IntPtr.Zero; // SBM_SETPOS(redraw=false)
            if (m.Msg == 0xE9) m.WParam = IntPtr.Zero; // SBM_SETSCROLLINFO(redraw=false)
            if (m.Msg == 0xE6) m.Msg = 0xE2; // SBM_SETRANGEREDRAW -> SBM_SETRANGE
        }
        if (Theme.Palette.IsDark && m.Msg is 0xA0 or 0x2A2)
        {
            var point = MessagePoint(m.LParam);
            if (m.Msg == 0x2A2 || ReadBar(-5) is ScrollInfo v && Contains(v, point) || ReadBar(-6) is ScrollInfo h && Contains(h, point))
            { m.Result = IntPtr.Zero; return; }
        }
        if (Theme.Palette.IsDark && m.Msg == 0x85 && PaintBars(IntPtr.Zero))
        {
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
        if (!Theme.Palette.IsDark) return;
        if (m.Msg == 0x317 && (((long)m.LParam & 2) != 0 || _owner is ScrollBar)) PaintBars(m.WParam);
        else if (m.Msg == 0x318 && _owner is ScrollBar) PaintBars(m.WParam);
        // 到达滚动边界后 WM_MOUSEWHEEL 仍可能重画原生箭头，且不会再产生 WM_SCROLL/WM_PAINT。
        else if (m.Msg is 0xF or 0x85 or 0x114 or 0x115 or 0xA1 or 0xA2 or 0x201 or 0x202 or 0x20A or 0x20E
            or 0xE0 or 0xE2 or 0xE4 or 0xE6 or 0xE9 or 0x100)
            PaintBars(IntPtr.Zero);
        if (m.Msg is 0x20A or 0x20E && _owner is DataGridView)
        {
            // 表格接收滚轮，实际滚动条是独立子 HWND；到边界时子窗口可能没有绘制消息。
            foreach (Control child in _owner.Controls)
                if (child is ScrollBar) Hooks.GetValue(child, c => new ThemeScrollbars(c)).PaintBars(IntPtr.Zero);
        }
    }

    private bool PaintBars(IntPtr suppliedDc)
    {
        var painted = false;
        UiHealth.TryRun("scrollbar-paint", () => painted = PaintBarsCore(suppliedDc));
        return painted;
    }

    private bool PaintBarsCore(IntPtr suppliedDc)
    {
        if (Handle == IntPtr.Zero || _owner.IsDisposed || !GetWindowRect(Handle, out var window)) return false;
        var vertical = _owner is ScrollBar ? _owner is VScrollBar ? ReadBar(-4) : null : ReadBar(-5);
        var horizontal = _owner is ScrollBar ? _owner is HScrollBar ? ReadBar(-4) : null : ReadBar(-6);
        if (vertical == null && horizontal == null) return false;
        var dc = suppliedDc != IntPtr.Zero ? suppliedDc : GetWindowDC(Handle);
        if (dc == IntPtr.Zero) return false;
        try
        {
            using var g = Graphics.FromHdc(dc);
            void Draw(ScrollInfo info, bool isVertical)
            {
                var r = Rectangle.FromLTRB(info.Bar.Left - window.Left, info.Bar.Top - window.Top,
                    info.Bar.Right - window.Left, info.Bar.Bottom - window.Top);
                var saved = g.Save();
                g.SetClip(r, CombineMode.Intersect);
                // 在内存中完成整条绘制，再一次性贴回；直接擦轨道再画滑块会短暂露出空轨道。
                using var frame = new Bitmap(r.Width, r.Height);
                using var canvas = Graphics.FromImage(frame);
                canvas.TranslateTransform(-r.Left, -r.Top);
                using var track = new SolidBrush(Theme.Canvas);
                canvas.FillRectangle(track, r);
                var inset = Math.Max(2, (int)Math.Round(3 * _owner.DeviceDpi / 96f));
                var thumb = isVertical
                    ? new Rectangle(r.Left + inset, r.Top + info.ThumbTop, Math.Max(1, r.Width - inset * 2), info.ThumbBottom - info.ThumbTop)
                    : new Rectangle(r.Left + info.ThumbTop, r.Top + inset, info.ThumbBottom - info.ThumbTop, Math.Max(1, r.Height - inset * 2));
                if (thumb.Width > 0 && thumb.Height > 0)
                {
                    canvas.SmoothingMode = SmoothingMode.AntiAlias;
                    using var path = ControlShape.Rounded(thumb, inset);
                    using var fill = new SolidBrush(Theme.Rule);
                    canvas.FillPath(fill, path);
                }
                using var arrow = new Pen(Theme.Muted, 1.25f * _owner.DeviceDpi / 96f)
                { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                var d = 3 * _owner.DeviceDpi / 96f;
                foreach (var end in new[] { false, true })
                {
                    var x = isVertical ? r.Left + r.Width / 2f : end ? r.Right - info.Button / 2f : r.Left + info.Button / 2f;
                    var y = isVertical ? end ? r.Bottom - info.Button / 2f : r.Top + info.Button / 2f : r.Top + r.Height / 2f;
                    var direction = end ? 1 : -1;
                    if (isVertical) canvas.DrawLines(arrow, [new PointF(x - d, y - direction * d / 2), new PointF(x, y + direction * d / 2), new PointF(x + d, y - direction * d / 2)]);
                    else canvas.DrawLines(arrow, [new PointF(x - direction * d / 2, y - d), new PointF(x + direction * d / 2, y), new PointF(x - direction * d / 2, y + d)]);
                }
                g.DrawImageUnscaled(frame, r.Location);
                g.Restore(saved);
            }
            if (vertical is ScrollInfo v) Draw(v, true);
            if (horizontal is ScrollInfo h) Draw(h, false);
            if (vertical is ScrollInfo vCorner && horizontal is ScrollInfo hCorner)
            {
                using var corner = new SolidBrush(Theme.Canvas);
                g.FillRectangle(corner, new Rectangle(vCorner.Bar.Left - window.Left, hCorner.Bar.Top - window.Top,
                    vCorner.Bar.Right - vCorner.Bar.Left, hCorner.Bar.Bottom - hCorner.Bar.Top));
            }
            return true;
        }
        finally { if (suppliedDc == IntPtr.Zero) ReleaseDC(Handle, dc); }
    }

    private ScrollInfo? ReadBar(int id)
    {
        var info = new ScrollInfo { Size = Marshal.SizeOf<ScrollInfo>(), State = new uint[6] };
        return GetScrollBarInfo(Handle, id, ref info) && (info.State[0] & 0x8000) == 0 &&
            info.Bar.Right > info.Bar.Left && info.Bar.Bottom > info.Bar.Top ? info : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ScrollInfo
    {
        internal int Size;
        internal Rect Bar;
        internal int Button, ThumbTop, ThumbBottom, Reserved;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] internal uint[] State;
    }
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct PaintInfo
    {
        internal IntPtr Dc;
        internal int Erase;
        internal Rect Paint;
        internal int Restore, Update;
        internal fixed byte Reserved[32];
    }
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr window, out PaintInfo paint);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EndPaint(IntPtr window, ref PaintInfo paint);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetScrollBarInfo(IntPtr window, int id, ref ScrollInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RedrawWindow(IntPtr window, IntPtr rect, IntPtr region, uint flags);
}
