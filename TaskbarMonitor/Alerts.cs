using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TaskbarMonitor;

/// <summary>
/// Watches every snapshot for values that stay above their critical threshold
/// for a minute and raises one popup per episode. A value stays suppressed
/// until it recovers, so a permanently hot core cannot re-beep every second.
/// </summary>
public static class Alerts
{
    private static readonly TimeSpan Hold = TimeSpan.FromSeconds(60);

    private sealed class Watch
    {
        public DateTime? Since;
        public bool Raised;
    }

    private static readonly Dictionary<string, Watch> Watches = new();
    private static Window? _popup;

    public static void Check(MetricsSnapshot snapshot, AppSettings settings)
    {
        if (!settings.AlertsEnabled)
        {
            foreach (var watch in Watches.Values)
            {
                watch.Since = null;
                watch.Raised = false;
            }
            return;
        }

        var now = DateTime.UtcNow;
        var raised = new List<string>();

        void Evaluate(string key, double value, double limit, string label, string format)
        {
            var hot = !double.IsNaN(value) && value >= limit;
            if (!Watches.TryGetValue(key, out var watch)) Watches[key] = watch = new Watch();

            if (!hot)
            {
                watch.Since = null;
                watch.Raised = false;
                return;
            }

            watch.Since ??= now;
            if (watch.Raised || now - watch.Since < Hold) return;

            watch.Raised = true;
            raised.Add($"{label} {format}");
        }

        Evaluate("cpu%", snapshot.CpuPercent, settings.CriticalPercent, "CPU usage", $"{snapshot.CpuPercent:0}%");
        Evaluate("gpu%", snapshot.GpuPercent, settings.CriticalPercent, "GPU usage", $"{snapshot.GpuPercent:0}%");

        var ram = snapshot.RamTotal > 0 ? snapshot.RamUsed * 100.0 / snapshot.RamTotal : double.NaN;
        Evaluate("ram%", ram, settings.CriticalPercent, "RAM usage", $"{ram:0}%");

        Evaluate("cpuT", snapshot.CpuTemp, settings.CriticalTempC, "CPU temperature", $"{snapshot.CpuTemp:0}°C");
        Evaluate("gpuT", snapshot.GpuTemp, settings.CriticalTempC, "GPU temperature", $"{snapshot.GpuTemp:0}°C");

        if (raised.Count > 0) Show(string.Join("   ·   ", raised));
    }

    /// <summary>A small borderless note above the primary taskbar that closes itself.</summary>
    private static void Show(string text)
    {
        SystemSounds.Exclamation.Play();

        if (_popup is { IsVisible: true }) _popup.Close();

        var popup = new Window
        {
            // Borderless: the title is never shown, it only makes the window
            // findable for verification.
            Title = "Taskbar Monitor alert",
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Focusable = false,
            SizeToContent = SizeToContent.WidthAndHeight
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "TASKBAR MONITOR",
            Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0x3B, 0x3B)),
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = text + "  —  above the red level for a minute",
            Foreground = Brushes.White,
            FontSize = 12.5,
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 300
        });

        popup.Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x20, 0x14, 0x16)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xF2, 0x3B, 0x3B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 11),
            Child = panel
        };

        var work = SystemParameters.WorkArea;
        popup.Loaded += (_, _) =>
        {
            popup.Left = work.Right - popup.ActualWidth - 12;
            popup.Top = work.Bottom - popup.ActualHeight - 8;
        };

        popup.Show();
        _popup = popup;
        popup.Closed += (_, _) => { if (ReferenceEquals(_popup, popup)) _popup = null; };

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += (_, _) => { timer.Stop(); popup.Close(); };
        timer.Start();
    }
}
