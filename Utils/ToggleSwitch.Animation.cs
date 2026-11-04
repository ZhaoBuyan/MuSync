using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MuSync.Utils;

internal sealed partial class ToggleSwitch
{
    private const double MotionDurationMs = 180;
    private Timer? _motionTimer;
    private long _motionStarted;
    private float _motionFrom;
    private float _motionTarget;
    private float _restingPosition;
    private bool _userClick;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter,
        [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);

    internal static bool SystemMotionEnabled => !SystemInformation.HighContrast && SystemInformation.UIEffectsEnabled &&
        SystemParametersInfo(0x1042, 0, out var enabled, 0) && enabled; // SPI_GETCLIENTAREAANIMATION

    internal bool IsAnimating => _motionTimer?.Enabled == true;
    internal float CurrentThumbPosition
    {
        get
        {
            if (!IsAnimating) return _restingPosition;
            var time = Math.Clamp(Stopwatch.GetElapsedTime(_motionStarted).TotalMilliseconds / MotionDurationMs, 0, 1);
            var eased = 1 - Math.Pow(1 - time, 3);
            return (float)(_motionFrom + (_motionTarget - _motionFrom) * eased);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        _userClick = true;
        try { base.OnClick(e); }
        finally { _userClick = false; }
    }

    private void ChangeMotionTarget(float from)
    {
        if (IsDisposed || Disposing) return;
        _motionTarget = Checked ? 1 : 0;
        if (!_userClick || !Visible || !Enabled || !IsHandleCreated || !SystemMotionEnabled ||
            Math.Abs(from - _motionTarget) < .001f)
        {
            FinishMotion();
            return;
        }
        _motionFrom = from;
        _motionStarted = Stopwatch.GetTimestamp();
        if (_motionTimer is null)
        {
            // UI-thread timer only while moving. No global timer-resolution change or worker thread.
            _motionTimer = new Timer { Interval = 15 };
            _motionTimer.Tick += (_, _) =>
            {
                if (!Visible || !Enabled || !IsHandleCreated ||
                    Stopwatch.GetElapsedTime(_motionStarted).TotalMilliseconds >= MotionDurationMs)
                    FinishMotion();
                else Invalidate();
            };
        }
        _motionTimer.Start();
        Invalidate();
    }

    private void FinishMotion()
    {
        _motionTimer?.Stop();
        _restingPosition = Checked ? 1 : 0;
        if (!IsDisposed && !Disposing) Invalidate();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible) FinishMotion();
    }

    protected override void OnParentVisibleChanged(EventArgs e)
    {
        base.OnParentVisibleChanged(e);
        if (!Visible) FinishMotion();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        FinishMotion();
        base.OnHandleDestroyed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _motionTimer?.Dispose(); _motionTimer = null; }
        base.Dispose(disposing);
    }

    private static Color BlendTrack(Color off, Color on, float position) => Color.FromArgb(
        (int)Math.Round(off.R + (on.R - off.R) * position),
        (int)Math.Round(off.G + (on.G - off.G) * position),
        (int)Math.Round(off.B + (on.B - off.B) * position));
}
