using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;

namespace TaskbarMonitor;

public partial class App : Application
{
    private const string MutexName = @"Local\TaskbarMonitor.Instance";
    private const string ShowEventName = @"Local\TaskbarMonitor.ShowSettings";
    private const string ExitEventName = @"Local\TaskbarMonitor.Exit";

    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;
    private static EventWaitHandle? _exitEvent;
    private static DispatcherTimer? _saveTimer;
    private static DispatcherTimer? _themeTimer;
    private static DispatcherTimer? _fullscreenTimer;
    private static bool? _lastSystemLight;
    private static bool? _lastAppsLight;
    private static bool _shuttingDown;

    public static AppSettings Settings { get; private set; } = new();
    public static Palette CurrentPalette { get; private set; } = Palette.Create();
    public static MetricsService Metrics { get; } = new();
    public static List<OverlayWindow> Overlays { get; } = new();
    public static SettingsWindow? SettingsWindowInstance { get; private set; }
    public static bool IsElevated { get; private set; }
    public static MetricsSnapshot Latest => Metrics.Latest;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, MutexName, out var firstInstance);
        if (!firstInstance)
        {
            if (e.Args.Contains("--replace", StringComparer.OrdinalIgnoreCase))
            {
                Signal(ExitEventName);
                try
                {
                    if (!_mutex.WaitOne(TimeSpan.FromSeconds(6)))
                    {
                        MessageBox.Show("The previous instance did not close. Please try again.",
                            "Taskbar Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
                        Current.Shutdown();
                        return;
                    }
                }
                catch (AbandonedMutexException)
                {
                    // previous instance crashed; we own the mutex now
                }
            }
            else
            {
                Signal(ShowEventName);
                Current.Shutdown();
                return;
            }
        }

        IsElevated = CheckElevated();
        Settings = SettingsStore.Load();
        Settings.Normalize();

        // A desktop with no battery cannot show the battery block.
        if (!BatterySampler.IsPresent())
        {
            var battery = Settings.Blocks.FirstOrDefault(b => b.Kind == MetricKind.Battery);
            if (battery is { Enabled: true }) battery.Enabled = false;
            if (!Settings.Blocks.Any(b => b.Enabled))
                Settings.Blocks.First(b => b.Kind != MetricKind.Battery).Enabled = true;
        }

        CurrentPalette = Palette.Create();
        SettingsStore.Save(Settings);

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("dispatcher", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("appdomain", args.ExceptionObject as Exception ?? new Exception("unknown failure"));

        Metrics.Updated += snapshot => Dispatcher.BeginInvoke(new Action(() =>
        {
            foreach (var overlay in Overlays) overlay.UpdateValues(snapshot);
            SettingsWindowInstance?.UpdatePreview(false);
        }));

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer!.Stop();
            SettingsStore.Save(Settings);
        };

        _themeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _themeTimer.Tick += (_, _) => FollowSystemTheme();
        _themeTimer.Start();

        _fullscreenTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _fullscreenTimer.Tick += (_, _) => Fullscreen.Refresh();
        _fullscreenTimer.Start();
        Fullscreen.Refresh();

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        new Thread(ListenForSignals) { IsBackground = true }.Start();

        Metrics.Start(Settings.RefreshMs);
        RebuildOverlays();

        if (!e.Args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase)))
            ShowSettings();
    }

    public static void ShowSettings()
    {
        if (SettingsWindowInstance is { IsVisible: true })
        {
            if (SettingsWindowInstance.WindowState == WindowState.Minimized)
                SettingsWindowInstance.WindowState = WindowState.Normal;
            SettingsWindowInstance.Activate();
            return;
        }

        SettingsWindowInstance = new SettingsWindow();
        SettingsWindowInstance.Closed += (_, _) => SettingsWindowInstance = null;
        SettingsWindowInstance.Show();
        // New windows from a background process cannot take the foreground by
        // themselves; the Topmost flip brings them forward without staying on top.
        SettingsWindowInstance.Activate();
        SettingsWindowInstance.Topmost = true;
        SettingsWindowInstance.Topmost = false;
    }

    public static void ApplySettings(bool rebuildBlocks = true)
    {
        Settings.Normalize();
        CurrentPalette = Palette.Create();
        Metrics.SetInterval(Settings.RefreshMs);

        foreach (var overlay in Overlays) overlay.Reload(rebuildBlocks);
        SettingsWindowInstance?.UpdatePreview(rebuildBlocks);
        QueueSave();
    }

    public static void ResetSettings()
    {
        Settings = new AppSettings();
        Settings.Normalize();
        SettingsStore.Save(Settings);
        RebuildOverlays();
        ApplySettings();
    }

    public static void RebuildOverlays()
    {
        foreach (var overlay in Overlays.ToList()) overlay.Close();
        Overlays.Clear();

        var taskbars = TaskbarLocator.GetAll();
        List<TaskbarInfo> targets;

        if (Settings.ShowOnAllMonitors)
        {
            targets = taskbars;
        }
        else
        {
            var chosen = taskbars.FirstOrDefault(t =>
                             string.Equals(t.MonitorDevice, Settings.MonitorDevice, StringComparison.OrdinalIgnoreCase))
                         ?? taskbars.FirstOrDefault(t => t.IsPrimary)
                         ?? taskbars.FirstOrDefault();

            targets = chosen is null ? new List<TaskbarInfo>() : new List<TaskbarInfo> { chosen };
        }

        if (targets.Count == 0)
        {
            // No taskbar window found: keep the primary monitor so the user still sees something.
            targets.Add(new TaskbarInfo { MonitorDevice = "" });
        }

        foreach (var target in targets)
        {
            var overlay = new OverlayWindow(target.MonitorDevice);
            overlay.Show();
            Overlays.Add(overlay);
        }
    }

    public static void QueueSave()
    {
        if (_saveTimer is null) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public static void TogglePause() => Metrics.Paused = !Metrics.Paused;

    public static void RestartElevated()
    {
        try
        {
            SettingsStore.Save(Settings);
            var path = Environment.ProcessPath;
            if (path is null) return;
            Process.Start(new ProcessStartInfo(path, "--replace")
            {
                UseShellExecute = true,
                Verb = "runas"
            });
            ExitApp();
        }
        catch (Exception ex)
        {
            Log.Error("restart elevated", ex);
        }
    }

    public static void ExitApp()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;

        SettingsStore.Save(Settings);
        Metrics.Dispose();

        foreach (var overlay in Overlays.ToList()) overlay.Close();
        Overlays.Clear();

        SettingsWindowInstance?.Close();
        SettingsWindowInstance = null;

        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        _mutex = null;

        Current.Shutdown();
    }

    private static void FollowSystemTheme()
    {
        var light = Palette.SystemUsesLightTheme();
        var appsLight = Palette.SystemAppsUseLightTheme();
        if (_lastSystemLight == light && _lastAppsLight == appsLight) return;
        _lastSystemLight = light;
        _lastAppsLight = appsLight;

        CurrentPalette = Palette.Create();
        foreach (var overlay in Overlays) overlay.Reload(rebuildBlocks: false);
        SettingsWindowInstance?.UpdatePreview(false);
        SettingsWindowInstance?.RefreshTheme();
    }

    private static void ListenForSignals()
    {
        var handles = new WaitHandle[] { _showEvent!, _exitEvent! };
        while (!_shuttingDown)
        {
            int index;
            try { index = WaitHandle.WaitAny(handles); }
            catch { break; }

            if (index == 0) Current.Dispatcher.BeginInvoke(new Action(ShowSettings));
            else
            {
                Current.Dispatcher.BeginInvoke(new Action(ExitApp));
                break;
            }
        }
    }

    private static void Signal(string name)
    {
        try { EventWaitHandle.OpenExisting(name).Set(); }
        catch { /* no other instance listening */ }
    }

    private static bool CheckElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
