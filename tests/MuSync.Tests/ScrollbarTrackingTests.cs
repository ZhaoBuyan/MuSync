using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

[Collection(LanguageSensitiveCollection.Name)]
public class ScrollbarTrackingTests
{
    [Fact]
    public void DarkScrollbarPaintValidatesRegionWithoutNativeOverpaintOrIdleLoop()
    {
        var previous = Theme.Palette.Id;
        using var host = Host();
        try
        {
            Theme.PreviewPalette(ThemePalettes.NightPrototype, host);
            var bar = new VScrollBar { Dock = DockStyle.Right, Maximum = 1000 };
            host.Controls.Add(bar); host.Show();
            using var native = new PaintObserver(bar.Handle);
            Theme.Apply(bar);
            bar.Invalidate();
            bar.Update();
            Assert.Equal(0, native.Paints);
            Assert.False(GetUpdateRect(bar.Handle, IntPtr.Zero, false));
            for (var i = 0; i < 10; i++) Application.DoEvents();
            Assert.False(GetUpdateRect(bar.Handle, IntPtr.Zero, false));
            Assert.Equal(0, native.Paints);
            Theme.SwitchPalette("fog", bar);
            bar.Invalidate(); bar.Update();
            Assert.True(native.Paints > 0);
        }
        finally { Theme.SwitchPalette(previous, host); }
    }

    private sealed class PaintObserver : NativeWindow, IDisposable
    {
        public int Paints;
        public PaintObserver(IntPtr handle) => AssignHandle(handle);
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0xF) Paints++;
            base.WndProc(ref m);
        }
        public void Dispose() => ReleaseHandle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DragRetainsCaptureAndFull32BitPositionUntilRelease(bool horizontal)
    {
        var previous = Theme.Palette.Id;
        using var host = Host();
        try
        {
            Theme.PreviewPalette(ThemePalettes.NightPrototype, host);
            ScrollBar bar = horizontal ? new HScrollBar { Dock = DockStyle.Bottom } : new VScrollBar { Dock = DockStyle.Right };
            bar.Maximum = 250000; bar.LargeChange = 1000;
            host.Controls.Add(bar);
            Theme.Apply(bar);
            var events = new List<ScrollEventType>();
            bar.Scroll += (_, e) => events.Add(e.Type);
            host.Show();
            var geometry = Read(bar, -4);
            var start = Thumb(geometry, horizontal);
            SendMessage(bar.Handle, 0x201, (IntPtr)1, Pack(bar.PointToClient(start)));
            Assert.True(bar.Capture); // 原生实现会在 MouseDown 内进入阻塞追踪循环。
            var end = horizontal ? new Point(geometry.Bounds.Right - geometry.Button - 1, start.Y)
                : new Point(start.X, geometry.Bounds.Bottom - geometry.Button - 1);
            SendMessage(bar.Handle, 0x200, (IntPtr)1, Pack(bar.PointToClient(end)));
            Assert.True(bar.Value > 65535);
            Assert.True(bar.Capture);
            Assert.Contains(ScrollEventType.ThumbTrack, events);
            SendMessage(bar.Handle, 0x202, IntPtr.Zero, Pack(bar.PointToClient(end)));
            Assert.False(bar.Capture);
            Assert.Equal(ScrollEventType.ThumbPosition, events[^2]);
            Assert.Equal(ScrollEventType.EndScroll, events[^1]);
        }
        finally { Theme.SwitchPalette(previous, host); }
    }

    [Fact]
    public void PanelDragScrollsBothAxesAndPaletteChangeReleasesCapture()
    {
        var previous = Theme.Palette.Id;
        using var host = Host();
        try
        {
            Theme.PreviewPalette(ThemePalettes.NightPrototype, host);
            var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, AutoScrollMinSize = new Size(2000, 150000) };
            host.Controls.Add(panel); Theme.Apply(panel); host.Show();
            foreach (var horizontal in new[] { false, true })
            {
                var geometry = Read(panel, horizontal ? -6 : -5);
                var start = Thumb(geometry, horizontal);
                SendMessage(panel.Handle, 0xA1, (IntPtr)(horizontal ? 6 : 7), Pack(start));
                Assert.True(panel.Capture);
                var end = horizontal ? new Point(geometry.Bounds.Right - geometry.Button - 1, start.Y)
                    : new Point(start.X, geometry.Bounds.Bottom - geometry.Button - 1);
                SendMessage(panel.Handle, 0x200, (IntPtr)1, Pack(panel.PointToClient(end)));
                Assert.True(horizontal ? -panel.AutoScrollPosition.X > 1000 : -panel.AutoScrollPosition.Y > 65535,
                    $"horizontal={horizontal}, position={panel.AutoScrollPosition}, max={panel.VerticalScroll.Maximum}, failures={UiHealth.Failures}");
                SendMessage(panel.Handle, 0x202, IntPtr.Zero, Pack(panel.PointToClient(end)));
            }
            var thumb = Thumb(Read(panel, -5), false);
            SendMessage(panel.Handle, 0xA1, (IntPtr)7, Pack(thumb));
            Assert.True(panel.Capture);
            Theme.SwitchPalette("fog", panel);
            Assert.False(panel.Capture);
        }
        finally { Theme.SwitchPalette(previous, host); }
    }

    [Fact]
    public void GridDragUsesItsOwnRowScrollHandlerAndHidingCancelsCapture()
    {
        var previous = Theme.Palette.Id;
        using var host = Host();
        try
        {
            Theme.PreviewPalette(ThemePalettes.NightPrototype, host);
            var grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false };
            grid.Columns.Add("value", "Value");
            for (var i = 0; i < 100; i++) grid.Rows.Add(i.ToString());
            grid.Rows[2].Height = 60;
            host.Controls.Add(grid); Theme.Apply(grid); host.Show();
            var bar = grid.Controls.OfType<VScrollBar>().Single();
            var info = Read(bar, -4);
            var start = Thumb(info, false);
            SendMessage(bar.Handle, 0x201, (IntPtr)1, Pack(bar.PointToClient(start)));
            Assert.True(bar.Capture);
            SendMessage(bar.Handle, 0x200, (IntPtr)1, Pack(bar.PointToClient(new Point(start.X, info.Bounds.Bottom - info.Button - 1))));
            Assert.True(grid.FirstDisplayedScrollingRowIndex > 50);
            grid.Hide();
            Assert.False(bar.Capture);
        }
        finally { Theme.SwitchPalette(previous, host); }
    }

    private static Form Host() => new() { StartPosition = FormStartPosition.Manual,
        Location = new Point(-20000, -20000), ClientSize = new Size(400, 300) };
    private static Point Thumb(BarInfo bar, bool horizontal) => horizontal
        ? new Point(bar.Bounds.Left + (bar.Top + bar.Bottom) / 2, bar.Bounds.Top + bar.Bounds.Height / 2)
        : new Point(bar.Bounds.Left + bar.Bounds.Width / 2, bar.Bounds.Top + (bar.Top + bar.Bottom) / 2);
    private static BarInfo Read(Control control, int id)
    {
        var info = new BarInfo { Size = Marshal.SizeOf<BarInfo>(), States = new uint[6] };
        Assert.True(GetScrollBarInfo(control.Handle, id, ref info));
        return info;
    }
    private static IntPtr Pack(Point p) => (IntPtr)unchecked((int)((uint)(ushort)p.X | ((uint)(ushort)p.Y << 16)));
    [StructLayout(LayoutKind.Sequential)]
    private struct BarInfo
    {
        public int Size;
        private int Left, Y, Right, BottomEdge;
        public int Button, Top, Bottom, Reserved;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public uint[] States;
        public readonly Rectangle Bounds => Rectangle.FromLTRB(Left, Y, Right, BottomEdge);
    }
    [DllImport("user32.dll")] private static extern bool GetScrollBarInfo(IntPtr window, int id, ref BarInfo info);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool GetUpdateRect(IntPtr window, IntPtr rect, bool erase);
}
