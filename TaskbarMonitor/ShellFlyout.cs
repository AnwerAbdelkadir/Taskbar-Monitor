using System.Diagnostics;
using System.Text;

namespace TaskbarMonitor;

/// <summary>
/// The shell's own flyouts (Start, search, quick settings, task view, tray
/// overflow) raise the primary taskbar above every topmost window for as long as
/// they are open: even a brand-new topmost window is covered, and HWND_TOP,
/// SetWindowPos toggles, hide/show, SetWindowBand and parenting into the bar all
/// fail to win, so nothing can be drawn over the strip. Positioning the block
/// above the bar for Start was tried and looks worse than the clean hide. The
/// readings therefore hide themselves while one of these flyouts is open, and the
/// secondary taskbars — never raised that way — keep drawing.
/// </summary>
internal static class ShellFlyout
{
    // The foreground window of each flyout, measured on 26200: Start/search is a
    // Windows.UI.Core.CoreWindow owned by SearchHost, quick settings is
    // ControlCenterWindow from ShellHost, and task view is explorer's XAML island.
    private static readonly HashSet<string> Classes = new(StringComparer.Ordinal)
    {
        "Windows.UI.Core.CoreWindow",
        "ControlCenterWindow",
        "XamlExplorerHostIslandWindow",
        "XamlExplorerHostIslandWindow_WASDK",
        "TopLevelWindowForOverflowXamlIsland"
    };

    private static readonly HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer",
        "SearchHost",
        "ShellHost",
        "ShellExperienceHost",
        "StartMenuExperienceHost",
        "TextInputHost"
    };

    private static uint _processId;
    private static string _processName = "";

    public static bool IsOpen()
    {
        var window = Native.GetForegroundWindow();
        if (window == IntPtr.Zero) return false;

        var root = Native.GetAncestor(window, Native.GA_ROOT);
        var className = new StringBuilder(64);
        Native.GetClassName(root, className, className.Capacity);
        if (!Classes.Contains(className.ToString())) return false;

        Native.GetWindowThreadProcessId(root, out var processId);
        return Hosts.Contains(ProcessName(processId));
    }

    private static string ProcessName(uint processId)
    {
        if (processId == _processId) return _processName;

        _processId = processId;
        try { _processName = Process.GetProcessById((int)processId).ProcessName; }
        catch { _processName = ""; }
        return _processName;
    }
}
