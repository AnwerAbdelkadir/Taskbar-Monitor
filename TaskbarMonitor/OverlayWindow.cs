using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace TaskbarMonitor;

public sealed class OverlayWindow : Window
{
    private readonly MeterView _view = new();
    private readonly string _monitorDevice;
    private readonly bool _primary;
    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _topmostTimer;
    private IntPtr _hwnd;
    private IntPtr _taskbarHwnd;
    private bool _shown = true;
    private RECT? _lastBar;
    private int? _lastTrayLeft;

    public OverlayWindow(string monitorDevice)
    {
        _monitorDevice = monitorDevice;
        _primary = string.Equals(WinForms.Screen.PrimaryScreen?.DeviceName, monitorDevice,
            StringComparison.OrdinalIgnoreCase);

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Focusable = false;
        Opacity = 0;

        _view.Apply(App.Settings, App.CurrentPalette);
        Content = _view;
        ContextMenu = CreateMenu();
        // The overlay never activates (WS_EX_NOACTIVATE), so a plain click on the
        // readings is free real estate for the history window.
        MouseLeftButtonDown += (_, _) => App.ShowGraphs();

        SizeChanged += (_, _) => Position();
        Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            Position();
            Opacity = 1;
        }), DispatcherPriority.Loaded);

        // 150 ms, not 500: the shell raises the taskbar above us for the length of
        // its button animations, and every tick this takes the top back. A slower
        // tick turns that into a visible blink.
        _positionTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background,
            (_, _) => Position(), Dispatcher);
        _positionTimer.Start();

        // The shell holds the bar up for only ~100 ms at a time, which 150 ms is
        // still too slow for, so a dedicated guard keeps the z-order clean without
        // repeating the placement work. 25 ms keeps the gap under two frames.
        _topmostTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(25), DispatcherPriority.Background,
            (_, _) => RaiseTopmost(), Dispatcher);
        _topmostTimer.Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        var style = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
        Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE,
            new IntPtr(style | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE));
    }

    public void UpdateValues(MetricsSnapshot snapshot) => _view.UpdateValues(snapshot);

    public void Reload(bool rebuildBlocks)
    {
        if (rebuildBlocks) _view.Apply(App.Settings, App.CurrentPalette);
        else _view.ApplyStyle(App.Settings, App.CurrentPalette);
        Dispatcher.BeginInvoke(new Action(Position), DispatcherPriority.Loaded);
    }

    private TaskbarInfo? FindTaskbar()
    {
        if (_taskbarHwnd != IntPtr.Zero)
        {
            var cached = TaskbarLocator.DescribeHandle(_taskbarHwnd);
            if (cached is not null &&
                string.Equals(cached.MonitorDevice, _monitorDevice, StringComparison.OrdinalIgnoreCase))
                return cached;
        }

        var found = TaskbarLocator.GetAll()
            .Where(t => string.Equals(t.MonitorDevice, _monitorDevice, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.IsPrimary)
            .FirstOrDefault();

        _taskbarHwnd = found?.Handle ?? IntPtr.Zero;
        return found;
    }

    private void Position()
    {
        if (_hwnd == IntPtr.Zero) return;

        // Windows keeps the taskbar window where it is for a full-screen program
        // (same rect, still WS_VISIBLE) and only lowers it below the covering
        // window, so IsHidden never notices. Enforce the option here, per
        // monitor, instead of only in the hidden-bar branch further down.
        if (!App.Settings.ShowInFullscreen && Fullscreen.ActiveOn(_monitorDevice))
        {
            SetVisible(false);
            return;
        }

        var taskbar = FindTaskbar();
        if (taskbar is null)
        {
            SetVisible(false);
            return;
        }

        // The shell holds the primary taskbar above every topmost window while its
        // flyouts are open (a fresh topmost window is covered too), so the block
        // cannot be drawn over the bar: hide it until the menu closes instead of
        // leaving it half-covered under the bar. Lifting the block above the bar
        // was tried and looks worse than the clean hide.
        if (_primary && ShellFlyout.IsOpen())
        {
            SetVisible(false);
            return;
        }

        if (!Native.GetWindowRect(_hwnd, out var current) || current.Width < 2 || current.Height < 2)
            return;

        var width = current.Width;
        var height = current.Height;
        var scale = Native.GetDpiForWindow(_hwnd) / 96.0;
        var gap = (int)Math.Round(App.Settings.GapFromTray * scale);
        var verticalOffset = (int)Math.Round(App.Settings.OffsetY * scale);
        var rightOffset = (int)Math.Round(App.Settings.OffsetFromRight * scale);

        RECT bar;
        int trayLeft;

        if (!taskbar.IsHidden)
        {
            bar = taskbar.Rect;
            trayLeft = taskbar.HasTray ? taskbar.TrayRect.Left : bar.Right;
            _lastBar = bar;
            _lastTrayLeft = trayLeft;
        }
        else if (App.Settings.ShowInFullscreen && Fullscreen.IsActive)
        {
            // A full-screen app hides the taskbar but the readings stay where the
            // bar was. Before we ever saw it, rebuild that strip from the monitor.
            bar = _lastBar ?? HiddenBar(taskbar);
            trayLeft = _lastTrayLeft ?? bar.Right;
        }
        else
        {
            SetVisible(false);
            return;
        }

        var verticalTaskbar = bar.Height > bar.Width;

        int x, y;
        if (verticalTaskbar)
        {
            // shortcut: vertical taskbars get a floating block beside them, not a rotated one
            x = bar.Left > 0 ? bar.Left - width - gap : bar.Right + gap;

            var bottom = App.Settings.OffsetFromRight > 0 ? bar.Bottom - rightOffset : bar.Bottom;
            y = ClampAxis(bottom - height - gap + verticalOffset, height, bar.Top, bar.Bottom);
        }
        else
        {
            // The gap from the tray applies to automatic and manual placement alike.
            var right = App.Settings.OffsetFromRight > 0
                ? Math.Min(bar.Right - rightOffset, trayLeft - gap)
                : trayLeft - gap;

            x = Math.Max(bar.Left, right - width);
            y = ClampAxis(bar.Top + (bar.Height - height) / 2 + verticalOffset, height, bar.Top, bar.Bottom);
        }

        // A borderless program that never trips the shell's full-screen state
        // still ends up over this strip; the rect comparison catches those.
        if (!App.Settings.ShowInFullscreen && CoversStrip(x, y, width, height))
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);
        if (Opacity < 1) Opacity = 1;

        // The shell raises the primary taskbar above topmost windows whenever it
        // animates it (window minimize/restore, thumbnails, flyouts), and a
        // topmost window never falls back below us on its own. Take the top back
        // every tick instead of only when the block moves, otherwise the
        // readings can stay hidden behind the bar indefinitely.
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

        if (x == current.Left && y == current.Top) return;

        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

        // The taskbar does not repaint the area a layered window vacates after a
        // move/resize, which leaves ghost text behind. Use the real window rect.
        var union = new RECT
        {
            Left = Math.Min(current.Left, x),
            Top = Math.Min(current.Top, y),
            Right = Math.Max(current.Right, x + width),
            Bottom = Math.Max(current.Bottom, y + height)
        };

        Native.RedrawWindow(taskbar.Handle, ref union, IntPtr.Zero,
            Native.RDW_INVALIDATE | Native.RDW_ERASE | Native.RDW_UPDATENOW | Native.RDW_ALLCHILDREN);
    }

    /// <summary>Recreates the strip a hidden taskbar occupies on its monitor.</summary>
    private static RECT HiddenBar(TaskbarInfo taskbar)
    {
        var monitor = taskbar.MonitorRect;
        var rect = taskbar.Rect;

        if (rect.Width >= rect.Height)
        {
            var thickness = Math.Max(24, rect.Height);
            return new RECT
            {
                Left = monitor.Left,
                Top = monitor.Bottom - thickness,
                Right = monitor.Right,
                Bottom = monitor.Bottom
            };
        }

        // Vertical taskbar: keep the side it sat on.
        var strip = Math.Max(24, rect.Width);
        var atRight = rect.Left > monitor.Left;
        return new RECT
        {
            Left = atRight ? monitor.Right - strip : monitor.Left,
            Top = monitor.Top,
            Right = atRight ? monitor.Right : monitor.Left + strip,
            Bottom = monitor.Bottom
        };
    }

    private static int ClampAxis(int value, int size, int start, int end)
    {
        // Keep the block inside the taskbar even with an extreme vertical offset.
        var max = Math.Max(start, end - size);
        return Math.Clamp(value, start, max);
    }

    /// <summary>
    /// True when the foreground program sits over the strip the readings would
    /// occupy. The same monitor only: a game on one screen must not hide the
    /// other taskbars, and the desktop or the shell never counts as a program.
    /// </summary>
    private static bool CoversStrip(int x, int y, int width, int height) =>
        Fullscreen.CoversForeground(x, y, width, height);

    private void SetVisible(bool visible)
    {
        if (_hwnd == IntPtr.Zero || visible == _shown) return;
        _shown = visible;
        Native.ShowWindow(_hwnd, visible ? Native.SW_SHOWNOACTIVATE : Native.SW_HIDE);
    }

    /// <summary>
    /// Stays at the top of the topmost band. The shell re-raises the taskbar on
    /// every button animation (minimize/restore, thumbnails) and a topmost window
    /// it lifted above us would cover the readings until the next placement tick.
    /// </summary>
    private void RaiseTopmost()
    {
        if (_hwnd == IntPtr.Zero || !_shown) return;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    private ContextMenu CreateMenu()
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) =>
        {
            menu.Items.Clear();

            var settings = new MenuItem { Header = "Settings…" };
            settings.Click += (_, _) => App.ShowSettings();
            menu.Items.Add(settings);

            var pause = new MenuItem
            {
                Header = "Pause readings",
                IsCheckable = true,
                IsChecked = App.Metrics.Paused
            };
            pause.Click += (_, _) => App.Metrics.Paused = pause.IsChecked;
            menu.Items.Add(pause);

            var startup = new MenuItem
            {
                Header = "Start with Windows",
                IsCheckable = true,
                IsChecked = App.Settings.StartWithWindows
            };
            startup.Click += (_, _) =>
            {
                App.Settings.StartWithWindows = startup.IsChecked;
                Startup.Apply(App.Settings.StartWithWindows, App.Settings.StartElevated);
                App.QueueSave();
            };
            menu.Items.Add(startup);

            var update = new MenuItem { Header = "Check for updates" };
            update.Click += (_, _) => UpdateWindow.ShowUpdate();
            menu.Items.Add(update);

            menu.Items.Add(new Separator());

            var exit = new MenuItem { Header = "Exit" };
            exit.Click += (_, _) => App.ExitApp();
            menu.Items.Add(exit);
        };
        return menu;
    }

    protected override void OnClosed(EventArgs e)
    {
        _positionTimer.Stop();
        _topmostTimer.Stop();

        var area = default(RECT);
        var hasArea = _hwnd != IntPtr.Zero && Native.GetWindowRect(_hwnd, out area) && area.Right > area.Left;

        base.OnClosed(e);

        // Layered windows leave their pixels on the taskbar when removed; ask it
        // to repaint the area we occupied.
        if (hasArea && _taskbarHwnd != IntPtr.Zero)
        {
            Native.RedrawWindow(_taskbarHwnd, ref area, IntPtr.Zero,
                Native.RDW_INVALIDATE | Native.RDW_ERASE | Native.RDW_UPDATENOW | Native.RDW_ALLCHILDREN);
        }
    }
}
