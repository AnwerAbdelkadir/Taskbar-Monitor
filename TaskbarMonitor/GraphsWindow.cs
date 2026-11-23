using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;

namespace TaskbarMonitor;

/// <summary>
/// Sparklines over the last two minutes, opened by left-clicking the readings.
/// Dark chrome in both themes, like the settings sidebar, so nothing has to be
/// re-themed when the Windows theme flips.
/// </summary>
public sealed class GraphsWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly List<Chart> _charts = new();

    private const double CanvasWidth = 340;
    private const double CanvasHeight = 42;

    private sealed record Chart(
        Canvas Canvas,
        TextBlock Value,
        Func<HistorySample, double>[] Lines,
        double FixedMax,
        Func<double, string> Format);

    public GraphsWindow()
    {
        Title = "Taskbar Monitor — history";
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 388;
        ShowInTaskbar = true;
        Background = new SolidColorBrush(Color.FromRgb(0x15, 0x16, 0x1A));
        Content = Build();

        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            (_, _) => Redraw(), Dispatcher);
        _timer.Start();
        Redraw();
    }

    private StackPanel Build()
    {
        var panel = new StackPanel { Margin = new Thickness(20, 14, 20, 18) };
        panel.Children.Add(new TextBlock
        {
            Text = "Last two minutes",
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold
        });

        static Func<HistorySample, double> Line(Func<HistorySample, double> line) => line;

        panel.Children.Add(BuildChart("CPU", new[] { Line(s => s.Cpu) }, 100, v => double.IsNaN(v) ? "--" : $"{v:0}%"));
        panel.Children.Add(BuildChart("GPU", new[] { Line(s => s.Gpu) }, 100, v => double.IsNaN(v) ? "--" : $"{v:0}%"));
        panel.Children.Add(BuildChart("RAM", new[] { Line(s => s.Ram) }, 100, v => double.IsNaN(v) ? "--" : $"{v:0}%"));
        panel.Children.Add(BuildChart("Network ↑ / ↓", new[] { Line(s => s.NetUp), Line(s => s.NetDown) }, 0, RateText));
        panel.Children.Add(BuildChart("CPU temperature", new[] { Line(s => s.CpuTemp) }, 100, v => double.IsNaN(v) ? "--" : $"{v:0}°C"));
        panel.Children.Add(BuildChart("GPU temperature", new[] { Line(s => s.GpuTemp) }, 100, v => double.IsNaN(v) ? "--" : $"{v:0}°C"));

        return panel;
    }

    private StackPanel BuildChart(string title, Func<HistorySample, double>[] lines, double fixedMax, Func<double, string> format)
    {
        var muted = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6));

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 3) };
        header.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = muted,
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center
        });
        var value = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 11.5,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(value);

        var canvas = new Canvas
        {
            Width = CanvasWidth,
            Height = CanvasHeight,
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0))
        };

        _charts.Add(new Chart(canvas, value, lines, fixedMax, format));

        var box = new StackPanel();
        box.Children.Add(header);
        box.Children.Add(canvas);
        return box;
    }

    private static string RateText(double bytesPerSecond)
    {
        if (!double.IsFinite(bytesPerSecond) || bytesPerSecond < 0) bytesPerSecond = 0;
        var mbps = bytesPerSecond * 8 / 1_000_000.0;
        return mbps >= 1 ? $"{mbps:0.0} Mbps" : $"{mbps:0.00} Mbps";
    }

    private static readonly Brush[] LineBrushes =
    {
        Brushes.White,
        new SolidColorBrush(Color.FromRgb(0x0E, 0xA5, 0xE9)),
    };

    private void Redraw()
    {
        var samples = History.Samples();
        var last = samples.Length > 0 ? samples[^1] : (HistorySample?)null;

        foreach (var chart in _charts)
        {
            chart.Canvas.Children.Clear();

            var max = chart.FixedMax;
            if (max <= 0)
            {
                // Dynamic scale (network): the busiest moment of the window.
                var observed = 0.0;
                foreach (var line in chart.Lines)
                    foreach (var sample in samples)
                    {
                        var v = line(sample);
                        if (double.IsFinite(v) && v > observed) observed = v;
                    }
                max = Math.Max(observed * 1.15, 1024); // at least 8 Kbps so a flat line is not a dot
            }

            for (var i = 0; i < chart.Lines.Length; i++)
            {
                var line = chart.Lines[i];
                var points = new PointCollection();
                for (var s = 0; s < samples.Length; s++)
                {
                    var v = line(samples[s]);
                    if (!double.IsFinite(v)) continue;
                    var x = samples.Length == 1 ? 0 : s * (CanvasWidth - 2) / (samples.Length - 1);
                    var y = CanvasHeight - Math.Clamp(v / max, 0, 1) * (CanvasHeight - 2) - 1;
                    points.Add(new Point(1 + x, y));
                }

                if (points.Count == 0) continue;

                chart.Canvas.Children.Add(new Polyline
                {
                    Points = points,
                    Stroke = LineBrushes[i % LineBrushes.Length],
                    StrokeThickness = 1.4,
                    StrokeLineJoin = PenLineJoin.Round
                });
            }

            var current = chart.Lines[0](last ?? new HistorySample(double.NaN, double.NaN, double.NaN, 0, 0, double.NaN, double.NaN));
            chart.Value.Text = chart.Format(current);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }
}
