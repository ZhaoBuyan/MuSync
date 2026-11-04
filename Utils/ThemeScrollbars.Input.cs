using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace MuSync.Utils;

internal sealed partial class ThemeScrollbars
{
    // 使用受保护的标准 Scroll 事件入口，保留 DataGridView 自己的像素/行高、编辑提交和范围处理。
    // 不读取框架私有字段，也不把 32 位位置塞进 WM_SCROLL 的 16 位参数。
    private static readonly Action<ScrollBar, ScrollEventArgs> RaiseBarScroll =
        typeof(ScrollBar).GetMethod("OnScroll", BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(ScrollEventArgs)], null)!.CreateDelegate<Action<ScrollBar, ScrollEventArgs>>();
    private static readonly Action<ScrollableControl, ScrollEventArgs> RaisePanelScroll =
        typeof(ScrollableControl).GetMethod("OnScroll", BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(ScrollEventArgs)], null)!.CreateDelegate<Action<ScrollableControl, ScrollEventArgs>>();

    private bool _tracking, _vertical;
    private int _part, _grabOffset;
    private Timer? _repeatTimer;

    private bool HandleMouse(ref Message m)
    {
        if (m.Msg == 0x215 || m.Msg == 0x1F) { EndTracking(); return false; } // capture lost / cancel mode
        if (!Theme.Palette.IsDark || !_owner.Enabled) { EndTracking(); return false; }
        if (_tracking && m.Msg == 0x202) { EndTracking(); return true; }
        if (_tracking && m.Msg == 0x200)
        {
            var point = _owner.PointToScreen(MessagePoint(m.LParam));
            UiHealth.TryRun("scrollbar-drag", () => { if (_part == 3) DragThumb(point); });
            PaintBars(IntPtr.Zero);
            return true;
        }
        if (m.Msg is not (0xA1 or 0x201 or 0xA3 or 0x203) ||
            _owner is not (ScrollableControl or ScrollBar)) return false;
        var screen = m.Msg is 0xA1 or 0xA3 ? MessagePoint(m.LParam) : _owner.PointToScreen(MessagePoint(m.LParam));
        var v = ReadBar(_owner is ScrollBar ? -4 : -5);
        var h = _owner is ScrollBar ? null : ReadBar(-6);
        var vertical = _owner is VScrollBar || _owner is not ScrollBar;
        ScrollInfo info;
        if (v is ScrollInfo vb && Contains(vb, screen)) info = vb;
        else if (h is ScrollInfo hb && Contains(hb, screen)) { info = hb; vertical = false; }
        else return false;
        _vertical = vertical;
        _part = HitPart(info, screen);
        if ((info.State[_part] & 1) != 0) return true;
        _grabOffset = Coordinate(screen) - Start(info) - info.ThumbTop;
        _tracking = true;
        _owner.Capture = true;
        if (_part != 3)
        {
            UiHealth.TryRun("scrollbar-step", () => Step(_part));
            if (_tracking && !_owner.IsDisposed)
            {
                _repeatTimer ??= CreateRepeatTimer();
                _repeatTimer.Interval = 400;
                _repeatTimer.Start();
            }
        }
        PaintBars(IntPtr.Zero);
        return true;
    }

    private Timer CreateRepeatTimer()
    {
        var timer = new Timer();
        timer.Tick += (_, _) =>
        {
            if (!_tracking || !_owner.Capture || !Theme.Palette.IsDark || !_owner.Visible || !_owner.Enabled)
            { EndTracking(); return; }
            timer.Interval = 50;
            var info = ReadBar(_owner is ScrollBar ? -4 : _vertical ? -5 : -6);
            if (info is ScrollInfo bar && Contains(bar, Cursor.Position) && HitPart(bar, Cursor.Position) == _part)
                UiHealth.TryRun("scrollbar-repeat", () => Step(_part));
            PaintBars(IntPtr.Zero);
        };
        return timer;
    }

    private void EndTracking()
    {
        _repeatTimer?.Stop();
        if (!_tracking) return;
        _tracking = false;
        if (_owner.Capture) _owner.Capture = false;
        if (!_owner.IsDisposed && !_owner.Disposing)
            UiHealth.TryRun("scrollbar-end", () =>
            {
                if (_part == 3) ApplyValue(Range().Value, ScrollEventType.ThumbPosition);
                ApplyValue(Range().Value, ScrollEventType.EndScroll);
            });
    }

    private void DragThumb(Point screen)
    {
        var info = ReadBar(_owner is ScrollBar ? -4 : _vertical ? -5 : -6);
        if (info is not ScrollInfo bar) return;
        var range = Range();
        var travel = (_vertical ? bar.Bar.Bottom - bar.Bar.Top : bar.Bar.Right - bar.Bar.Left)
            - 2 * bar.Button - (bar.ThumbBottom - bar.ThumbTop);
        if (travel <= 0) return;
        var pixel = Math.Clamp(Coordinate(screen) - Start(bar) - _grabOffset - bar.Button, 0, travel);
        var value = (int)(range.Min + (long)Math.Round(pixel / (double)travel * ((long)range.Max - range.Min)));
        if (!_vertical && _owner is ScrollBar { RightToLeft: RightToLeft.Yes }) value = (int)((long)range.Max - value + range.Min);
        ApplyValue(value, ScrollEventType.ThumbTrack);
    }

    private void Step(int part)
    {
        var range = Range();
        var amount = part is 1 or 5 ? range.Small : range.Large;
        var type = part switch { 1 => ScrollEventType.SmallDecrement, 2 => ScrollEventType.LargeDecrement,
            4 => ScrollEventType.LargeIncrement, _ => ScrollEventType.SmallIncrement };
        var direction = part < 3 ? -1 : 1;
        if (!_vertical && _owner is ScrollBar { RightToLeft: RightToLeft.Yes }) direction = -direction;
        ApplyValue((int)Math.Clamp((long)range.Value + direction * (long)amount, range.Min, range.Max), type);
    }

    private (int Min, int Max, int Value, int Small, int Large) Range()
    {
        if (_owner is ScrollBar bar)
            return (bar.Minimum, Math.Max(bar.Minimum, bar.Maximum - bar.LargeChange + 1), bar.Value, bar.SmallChange, bar.LargeChange);
        var panel = (ScrollableControl)_owner;
        var scroll = _vertical ? (ScrollProperties)panel.VerticalScroll : panel.HorizontalScroll;
        return (scroll.Minimum, Math.Max(scroll.Minimum, scroll.Maximum - scroll.LargeChange + 1), scroll.Value, scroll.SmallChange, scroll.LargeChange);
    }

    private void ApplyValue(int value, ScrollEventType type)
    {
        var range = Range();
        value = Math.Clamp(value, range.Min, range.Max);
        var args = new ScrollEventArgs(type, range.Value, value,
            _vertical ? ScrollOrientation.VerticalScroll : ScrollOrientation.HorizontalScroll);
        if (_owner is ScrollBar bar)
        {
            RaiseBarScroll(bar, args);
            // 和原生 ScrollBar 一样，允许订阅者修正 NewValue。
            var updated = Range();
            bar.Value = Math.Clamp(args.NewValue, updated.Min, updated.Max);
        }
        else if (_owner is ScrollableControl panel)
        {
            var position = panel.AutoScrollPosition;
            panel.AutoScrollPosition = _vertical ? new Point(-position.X, value) : new Point(value, -position.Y);
            RaisePanelScroll(panel, args);
        }
    }

    private int Coordinate(Point p) => _vertical ? p.Y : p.X;
    private int Start(ScrollInfo bar) => _vertical ? bar.Bar.Top : bar.Bar.Left;
    private int HitPart(ScrollInfo bar, Point p)
    {
        var offset = Coordinate(p) - Start(bar);
        var length = _vertical ? bar.Bar.Bottom - bar.Bar.Top : bar.Bar.Right - bar.Bar.Left;
        return offset < bar.Button ? 1 : offset >= length - bar.Button ? 5 :
            offset < bar.ThumbTop ? 2 : offset < bar.ThumbBottom ? 3 : 4;
    }
    private static bool Contains(ScrollInfo bar, Point p) => Rectangle.FromLTRB(bar.Bar.Left, bar.Bar.Top, bar.Bar.Right, bar.Bar.Bottom).Contains(p);
    private static Point MessagePoint(IntPtr value) => new(unchecked((short)(long)value), unchecked((short)((long)value >> 16)));
}
