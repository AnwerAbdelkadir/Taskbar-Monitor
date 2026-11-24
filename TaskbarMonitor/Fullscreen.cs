using System.Runtime.InteropServices;
using System.Text;

namespace TaskbarMonitor;

/// <summary>
/// Tells whether a program is covering a monitor, which is what a full-screen
/// app looks like to the overlay. Windows never moves or hides the taskbar for
/// one, so the bar's rect alone cannot answer the question.
/// </summary>
public static class Fullscreen
{
    private const int FrameTolerance = 2;

    private static bool _shellBusy;
    private static string _logged = "";

    /// <summary>Monitors a covering program sits on; empty when none is known.</summary>
    private static readonly HashSet<string> Devices = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsActive => _shellBusy || Devices.Count > 0;

    /// <summary>True when a full-screen program covers <paramref name="device"/>.</summary>
    public static bool ActiveOn(string device) =>
        IsActive && (device.Length == 0 || Devices.Count == 0 || Devices.Contains(device));

    /// <summary>Refreshed about once a second from the UI thread.</summary>
    public static void Refresh()
    {
        // The shell keeps this state for full-screen games, players and browsers.
        var shellBusy = Native.SHQueryUserNotificationState(out var state) == 0 &&
                        (state == Native.QUNS_BUSY || state == Native.QUNS_RUNNING_D3D_FULL_SCREEN);

        // Borderless windows do not always set that state, so measure the
        // foreground window against the monitor it sits on.
        Devices.Clear();
        foreach (var device in CoveringDevices(shellBusy)) Devices.Add(device);
        _shellBusy = shellBusy;

        // Logged on the transition only; this runs once a second forever.
        var signature = $"{(shellBusy ? "busy" : "idle")}|{string.Join(",", Devices)}";
        if (signature == _logged) return;
        _logged = signature;
        Log.Info($"fullscreen: state={(IsActive ? "active" : "none")} " +
                 $"shell={(shellBusy ? "fullscreen" : "desktop")} " +
                 $"devices={(Devices.Count == 0 ? "-" : string.Join(",", Devices))}");
    }

    /// <summary>
    /// A covering foreground window is the full-screen program the user sees.
    /// When the shell reports full screen but the user is still working on
    /// another monitor, find the covering window instead of hiding every taskbar.
    /// </summary>
    private static List<string> CoveringDevices(bool shellBusy)
    {
        var foreground = DeviceOf(Native.GetForegroundWindow());
        if (foreground.Length > 0) return new List<string> { foreground };
        if (!shellBusy) return new List<string>();

        var devices = new List<string>();
        Native.EnumWindows((window, _) =>
        {
            if (!Native.IsWindowVisible(window)) return true;
            var device = DeviceOf(window);
            if (device.Length > 0 && !devices.Contains(device, StringComparer.OrdinalIgnoreCase))
                devices.Add(device);
            return devices.Count < 4;
        }, IntPtr.Zero);

        return devices;
    }

    /// <summary>Device name of the window when it covers its monitor; empty otherwise.</summary>
    private static string DeviceOf(IntPtr window)
    {
        if (window == IntPtr.Zero) return "";

        Native.GetWindowThreadProcessId(window, out var processId);
        if (processId == Environment.ProcessId) return "";

        if (!CoversMonitor(window)) return "";

        var monitor = Native.MonitorFromWindow(window, Native.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFOEX { Size = Marshal.SizeOf<MONITORINFOEX>() };
        return Native.GetMonitorInfo(monitor, ref info) ? info.Device ?? "" : "";
    }

    /// <summary>True when a program other than the desktop or the shell sits over an area.</summary>
    public static bool CoversForeground(int left, int top, int width, int height)
    {
        var window = Native.GetForegroundWindow();
        if (window == IntPtr.Zero) return false;

        Native.GetWindowThreadProcessId(window, out var processId);
        if (processId == Environment.ProcessId) return false;

        // Clicking the desktop makes Progman the foreground window, and it spans
        // every monitor, so without this the strip would look covered all day.
        if (IsShellSurface(window)) return false;

        if (!Native.GetWindowRect(window, out var rect)) return false;

        return rect.Left <= left + FrameTolerance &&
               rect.Top <= top + FrameTolerance &&
               rect.Right >= left + width - FrameTolerance &&
               rect.Bottom >= top + height - FrameTolerance;
    }

    private static bool CoversMonitor(IntPtr window)
    {
        // The desktop and the taskbar itself span the monitor but are not apps.
        if (IsShellSurface(window)) return false;

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

    private static bool IsShellSurface(IntPtr window)
    {
        var className = new StringBuilder(64);
        Native.GetClassName(window, className, className.Capacity);
        return className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }
}
