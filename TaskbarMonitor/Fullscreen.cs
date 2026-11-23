using System.Runtime.InteropServices;
using System.Text;

namespace TaskbarMonitor;

/// <summary>
/// Tells whether a program is currently covering its monitor, which is the only
/// case where the overlay keeps drawing while the taskbar is hidden.
/// </summary>
public static class Fullscreen
{
    private const int FrameTolerance = 2;

    public static bool IsActive { get; private set; }

    /// <summary>Refreshed about once a second from the UI thread.</summary>
    public static void Refresh()
    {
        if (!App.Settings.ShowInFullscreen)
        {
            IsActive = false;
            return;
        }

        // The shell keeps this state for full-screen games, players and browsers.
        if (Native.SHQueryUserNotificationState(out var state) == 0 &&
            (state == Native.QUNS_BUSY || state == Native.QUNS_RUNNING_D3D_FULL_SCREEN))
        {
            IsActive = true;
            return;
        }

        // Borderless windows do not always set that state, so measure the
        // foreground window against the monitor it sits on.
        IsActive = CoversForegroundMonitor();
    }

    private static bool CoversForegroundMonitor()
    {
        var window = Native.GetForegroundWindow();
        if (window == IntPtr.Zero) return false;

        Native.GetWindowThreadProcessId(window, out var processId);
        if (processId == Environment.ProcessId) return false;

        // The desktop and the taskbar itself span the monitor but are not apps.
        var className = new StringBuilder(64);
        Native.GetClassName(window, className, className.Capacity);
        switch (className.ToString())
        {
            case "Progman":
            case "WorkerW":
            case "Shell_TrayWnd":
            case "Shell_SecondaryTrayWnd":
                return false;
        }

        if (!Native.GetWindowRect(window, out var rect)) return false;

        // A maximized window has a caption; a full-screen one does not. Without
        // this a maximised app on an auto-hidden taskbar would count.
        var style = Native.GetWindowLongPtr(window, Native.GWL_STYLE).ToInt64();
        if ((style & Native.WS_CAPTION) == Native.WS_CAPTION) return false;

        var monitor = Native.MonitorFromWindow(window, Native.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFOEX { Size = Marshal.SizeOf<MONITORINFOEX>() };
        if (!Native.GetMonitorInfo(monitor, ref info)) return false;

        var area = info.Monitor;
        return rect.Left <= area.Left + FrameTolerance &&
               rect.Top <= area.Top + FrameTolerance &&
               rect.Right >= area.Right - FrameTolerance &&
               rect.Bottom >= area.Bottom - FrameTolerance;
    }
}
