using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Windows.Automation;
using Microsoft.Win32;

namespace TaskbarMonitor;

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left, Top, Right, Bottom;
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

[StructLayout(LayoutKind.Sequential)]
public struct FILETIME
{
    public uint Low;
    public uint High;
    public ulong ToUInt64 => ((ulong)High << 32) | Low;
}

[StructLayout(LayoutKind.Sequential)]
public struct MEMORYSTATUSEX
{
    public uint Length;
    public uint MemoryLoad;
    public ulong TotalPhys;
    public ulong AvailPhys;
    public ulong TotalPageFile;
    public ulong AvailPageFile;
    public ulong TotalVirtual;
    public ulong AvailVirtual;
    public ulong AvailExtendedVirtual;
}

[StructLayout(LayoutKind.Sequential)]
public struct SYSTEM_POWER_STATUS
{
    public byte ACLineStatus;
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte SystemStatusFlag;
    public int BatteryLifeTime;
    public int BatteryFullLifeTime;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct MONITORINFOEX
{
    public int Size;
    public RECT Monitor;
    public RECT Work;
    public uint Flags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string Device;
}

internal static class Native
{
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const long WS_CAPTION = 0x00C00000;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_NOACTIVATE = 0x08000000;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint GA_ROOT = 2;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern uint PdhOpenQuery(string? source, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    public static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    public static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type,
        out PDH_FMT_COUNTERVALUE value);

    [DllImport("pdh.dll")]
    public static extern uint PdhCloseQuery(IntPtr query);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    public static extern bool RedrawWindow(IntPtr hWnd, ref RECT updateRect, IntPtr updateRegion, uint flags);

    public const uint RDW_INVALIDATE = 0x0001;
    public const uint RDW_ERASE = 0x0004;
    public const uint RDW_ALLCHILDREN = 0x0080;
    public const uint RDW_UPDATENOW = 0x0100;

    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public const int QUNS_BUSY = 2;
    public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;

    public const uint PDH_FMT_DOUBLE = 0x00000200;

    [StructLayout(LayoutKind.Sequential)]
    public struct PDH_FMT_COUNTERVALUE
    {
        public uint Status;
        public double Value;
    }
}

public sealed class TaskbarInfo
{
    public IntPtr Handle { get; init; }
    public RECT Rect { get; init; }
    public RECT MonitorRect { get; init; }
    public RECT TrayRect { get; init; }
    public bool HasTray { get; init; }
    public bool IsPrimary { get; init; }
    public bool IsHidden { get; init; }
    public string MonitorDevice { get; init; } = "";
}

public static class TaskbarLocator
{
    private const string PrimaryClass = "Shell_TrayWnd";
    private const string SecondaryClass = "Shell_SecondaryTrayWnd";
    private const string TrayClass = "TrayNotifyWnd";

    // The Windows 11 taskbar is XAML, so its tray has no child windows left to
    // measure; UI Automation is the only reliable source and it is not cheap.
    // Probe once in a while instead of on every 500 ms positioning tick.
    private const long TrayProbeTtlMs = 5000;

    private static readonly object TrayProbeGate = new();
    private static readonly Dictionary<IntPtr, (int Left, long At)> TrayProbes = new();
    private static bool _trayProbeFailureLogged;

    public static List<TaskbarInfo> GetAll()
    {
        var list = new List<TaskbarInfo>();

        var primary = Native.FindWindow(PrimaryClass, null);
        if (primary != IntPtr.Zero)
        {
            var info = Describe(primary, true);
            if (info != null) list.Add(info);
        }

        Native.EnumWindows((hWnd, _) =>
        {
            var name = new StringBuilder(64);
            Native.GetClassName(hWnd, name, name.Capacity);
            if (name.ToString() != SecondaryClass || !Native.IsWindowVisible(hWnd)) return true;
            var info = Describe(hWnd, false);
            if (info != null) list.Add(info);
            return true;
        }, IntPtr.Zero);

        return list;
    }

    public static TaskbarInfo? DescribeHandle(IntPtr hWnd, bool isPrimary = false) => Describe(hWnd, isPrimary);

    private static TaskbarInfo? Describe(IntPtr hWnd, bool isPrimary)
    {
        if (!Native.GetWindowRect(hWnd, out var rect) || rect.Width <= 0 || rect.Height <= 0)
            return null;

        var monitor = Native.MonitorFromWindow(hWnd, Native.MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFOEX { Size = Marshal.SizeOf<MONITORINFOEX>() };
        if (!Native.GetMonitorInfo(monitor, ref mi)) return null;

        var horizontal = rect.Width >= rect.Height;

        var trayHandle = Native.FindWindowEx(hWnd, IntPtr.Zero, TrayClass, null);
        var tray = default(RECT);
        var hasTray = trayHandle != IntPtr.Zero &&
                      Native.GetWindowRect(trayHandle, out tray) &&
                      tray.Width > 40 && tray.Width < rect.Width;

        if (!hasTray)
        {
            tray = default;

            // Windows 11 keeps the tray inside the XAML taskbar and leaves
            // TrayNotifyWnd as a 0x0 placeholder, so measure the tray buttons
            // through UI Automation. Vertical bars float beside the bar and do
            // not use the tray rect.
            if (horizontal)
            {
                var trayLeft = ProbeTrayLeft(hWnd);
                if (trayLeft != int.MinValue && trayLeft > rect.Left && trayLeft < rect.Right)
                {
                    tray = new RECT
                    {
                        Left = trayLeft,
                        Top = rect.Top,
                        Right = rect.Right,
                        Bottom = rect.Bottom
                    };
                    hasTray = true;
                }
            }
        }

        var visible = horizontal
            ? Math.Min(rect.Bottom, mi.Monitor.Bottom) - Math.Max(rect.Top, mi.Monitor.Top)
            : Math.Min(rect.Right, mi.Monitor.Right) - Math.Max(rect.Left, mi.Monitor.Left);

        return new TaskbarInfo
        {
            Handle = hWnd,
            Rect = rect,
            MonitorRect = mi.Monitor,
            TrayRect = tray,
            HasTray = hasTray,
            IsPrimary = isPrimary,
            IsHidden = visible <= 4,
            MonitorDevice = mi.Device
        };
    }

    /// <summary>Left edge of the tray, or <see cref="int.MinValue"/> when unknown.</summary>
    private static int ProbeTrayLeft(IntPtr barHwnd)
    {
        var now = Environment.TickCount64;
        lock (TrayProbeGate)
        {
            if (TrayProbes.TryGetValue(barHwnd, out var cached) && now - cached.At < TrayProbeTtlMs)
                return cached.Left;
        }

        var left = QueryTrayLeft(barHwnd);

        lock (TrayProbeGate)
        {
            // Explorer re-creates taskbars; keep the cache from growing forever.
            if (TrayProbes.Count > 16) TrayProbes.Clear();
            TrayProbes[barHwnd] = (left, now);
        }

        return left;
    }

    private static int QueryTrayLeft(IntPtr barHwnd)
    {
        try
        {
            var bar = AutomationElement.FromHandle(barHwnd);
            if (bar is null) return int.MinValue;

            var found = int.MinValue;
            foreach (AutomationElement element in bar.FindAll(TreeScope.Descendants, Condition.TrueCondition))
            {
                string? className;
                System.Windows.Rect bounds;
                try
                {
                    var current = element.Current;
                    className = current.ClassName;
                    bounds = current.BoundingRectangle;
                }
                catch (ElementNotAvailableException)
                {
                    continue;
                }

                // The tray buttons expose themselves as SystemTray.*; the clock on
                // a secondary taskbar counts too, so the block stays clear of it.
                if (string.IsNullOrEmpty(className) ||
                    !className.StartsWith("SystemTray.", StringComparison.Ordinal) ||
                    bounds.IsEmpty || bounds.Width <= 0)
                    continue;

                var left = (int)Math.Round(bounds.Left);
                if (found == int.MinValue || left < found) found = left;
            }

            return found;
        }
        catch (Exception ex)
        {
            if (!_trayProbeFailureLogged)
            {
                _trayProbeFailureLogged = true;
                Log.Error("tray probe", ex);
            }

            return int.MinValue;
        }
    }
}

public static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "TaskbarMonitor";
    private const string TaskName = "TaskbarMonitor";

    private static string ExePath => Environment.ProcessPath ?? "";

    public static void Apply(bool enabled, bool elevated)
    {
        RemoveRunKey();
        DeleteTask();

        if (!enabled || ExePath.Length == 0) return;

        if (elevated)
        {
            // /RL HIGHEST + ONLOGON = no UAC prompt on every login (created once, as admin)
            ImportTask();
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ??
                            Registry.CurrentUser.CreateSubKey(RunKey);
            key?.SetValue(RunValue, $"\"{ExePath}\" --silent");
        }
    }

    /// <summary>
    /// Imports a task definition instead of <c>schtasks /Create</c>: the command line
    /// version hard-codes "do not start on batteries" and a 72-hour run limit, which
    /// would keep the overlay from starting on a laptop and kill it after three days.
    /// </summary>
    private static void ImportTask()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? "";
        var exe = SecurityElement.Escape(ExePath) ?? "";
        var xml = $"""
                   <?xml version="1.0" encoding="UTF-16"?>
                   <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
                     <RegistrationInfo>
                       <Description>Taskbar Monitor overlay at logon</Description>
                     </RegistrationInfo>
                     <Triggers>
                       <LogonTrigger>
                         <Enabled>true</Enabled>
                         <UserId>{sid}</UserId>
                       </LogonTrigger>
                     </Triggers>
                     <Principals>
                       <Principal id="Author">
                         <UserId>{sid}</UserId>
                         <LogonType>InteractiveToken</LogonType>
                         <RunLevel>HighestAvailable</RunLevel>
                       </Principal>
                     </Principals>
                     <Settings>
                       <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                       <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                       <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                       <AllowHardTerminate>true</AllowHardTerminate>
                       <StartWhenAvailable>false</StartWhenAvailable>
                       <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                       <IdleSettings>
                         <StopOnIdleEnd>false</StopOnIdleEnd>
                         <RestartOnIdle>false</RestartOnIdle>
                       </IdleSettings>
                       <AllowStartOnDemand>true</AllowStartOnDemand>
                       <Enabled>true</Enabled>
                       <Hidden>false</Hidden>
                       <RunOnlyIfIdle>false</RunOnlyIfIdle>
                       <WakeToRun>false</WakeToRun>
                       <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                       <Priority>7</Priority>
                     </Settings>
                     <Actions Context="Author">
                       <Exec>
                         <Command>"{exe}"</Command>
                         <Arguments>--silent</Arguments>
                       </Exec>
                     </Actions>
                   </Task>
                   """;

        var file = Path.Combine(Path.GetTempPath(), "TaskbarMonitor.task.xml");
        try
        {
            File.WriteAllText(file, xml, Encoding.Unicode);
            Run("schtasks.exe", $"/Create /TN \"{TaskName}\" /XML \"{file}\" /F");
        }
        catch (Exception ex) { Log.Error("startup task import", ex); }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    public static bool IsTaskInstalled() =>
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks", TaskName));

    private static void RemoveRunKey()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(RunValue, throwOnMissingValue: false);
        }
        catch (Exception ex) { Log.Error("startup run-key", ex); }
    }

    private static void DeleteTask()
    {
        try
        {
            if (IsTaskInstalled()) Run("schtasks.exe", $"/Delete /TN \"{TaskName}\" /F");
        }
        catch (Exception ex) { Log.Error("startup task delete", ex); }
    }

    private static void Run(string file, string arguments)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            p?.WaitForExit(5000);
        }
        catch (Exception ex) { Log.Error("startup " + arguments, ex); }
    }
}
