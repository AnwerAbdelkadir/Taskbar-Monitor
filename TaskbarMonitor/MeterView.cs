using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Path = System.Windows.Shapes.Path;

namespace TaskbarMonitor;

internal static class Icons
{
    // 16x16 filled silhouettes so they stay crisp at 12-13 px on every DPI.
    public static Geometry Get(MetricKind kind) => Geometry.Parse(kind switch
    {
        MetricKind.Cpu =>
            "M3,3 H13 V13 H3 Z M4.6,4.6 V11.4 H11.4 V4.6 Z M5.8,5.8 H10.2 V10.2 H5.8 Z " +
            "M2,5.5 H3 V6.5 H2 Z M2,7.5 H3 V8.5 H2 Z M2,9.5 H3 V10.5 H2 Z " +
            "M13,5.5 H14 V6.5 H13 Z M13,7.5 H14 V8.5 H13 Z M13,9.5 H14 V10.5 H13 Z " +
            "M5.5,2 H6.5 V3 H5.5 Z M7.5,2 H8.5 V3 H7.5 Z M9.5,2 H10.5 V3 H9.5 Z " +
            "M5.5,13 H6.5 V14 H5.5 Z M7.5,13 H8.5 V14 H7.5 Z M9.5,13 H10.5 V14 H9.5 Z",
        MetricKind.Gpu =>
            "M1,5 H15 V11 H1 Z M2.2,6.2 V9.8 H13.8 V6.2 Z M1,11.5 V14 H5 V12.6 H2.4 V11.5 Z " +
            "M10.5,6.4 A1.6,1.6 0 1 1 10.5,9.6 A1.6,1.6 0 1 1 10.5,6.4 Z " +
            "M10.2,7.7 H10.8 V8.3 H10.2 Z M3.5,7 H6 V7.8 H3.5 Z M3.5,8.6 H6 V9.4 H3.5 Z",
        MetricKind.Ram =>
            "M5,2 H11 V13 H5 Z M6.2,3.2 V11.8 H9.8 V3.2 Z M6.5,5 H9.5 V6 H6.5 Z " +
            "M6,13.5 H7 V15 H6 Z M8,13.5 H9 V15 H8 Z M10,13.5 H11 V15 H10 Z " +
            "M5,1 H6 V2 H5 Z M8,1 H9 V2 H8 Z",
        MetricKind.Vram => "M3,4 H13 V12 H3 Z M4.2,5.2 V10.8 H11.8 V5.2 Z M5.4,6.2 H10.6 V9.8 H5.4 Z " +
            "M1.5,6 H3 V7 H1.5 Z M1.5,9 H3 V10 H1.5 Z M13,6 H14.5 V7 H13 Z M13,9 H14.5 V10 H13 Z",
        MetricKind.Network => "M1,7 L5,3 L9,7 H6.5 V13 H3.5 V7 Z M7,9 L11,13 L15,9 H12.5 V3 H9.5 V9 Z",
        MetricKind.DiskActivity =>
            "M8,2 A6,6 0 1 1 7.99,2 Z M8,4.2 A3.8,3.8 0 1 0 8,11.8 A3.8,3.8 0 1 0 8,4.2 Z " +
            "M7.4,7.4 H8.6 V8.6 H7.4 Z M8,8 L11,5.4 V6.8 L8.6,8.8 Z",
        MetricKind.DiskSpace =>
            "M2,5 H14 V11 H2 Z M3,6 V10 H13 V6 Z M2,4 H14 V5 H2 Z M4,11.5 H5.5 V13 H4 Z M10.5,11.5 H12 V13 H10.5 Z M6,6.6 H10 V7.4 H6 Z",
        MetricKind.Disk =>
            "M8,2 A6,6 0 1 1 7.99,2 Z M8,4.2 A3.8,3.8 0 1 0 8,11.8 A3.8,3.8 0 1 0 8,4.2 Z " +
            "M7.4,7.4 H8.6 V8.6 H7.4 Z M8,8 L11,5.4 V6.8 L8.6,8.8 Z",
        MetricKind.Battery =>
            "M1,4 H12 V12 H1 Z M2,5 V11 H11 V5 Z M12,6.5 H14 V9.5 H12 Z M3,6 H9 V10 H3 Z",
        _ => "M0,0 H1 V1 H0 Z"
    });

    public static Color ColorFor(MetricKind kind) => kind switch
    {
        MetricKind.Cpu => Color.FromRgb(0x2D, 0x7F, 0xF9),
        MetricKind.Gpu => Color.FromRgb(0x43, 0xA0, 0x47),
        MetricKind.Ram => Color.FromRgb(0x7E, 0x57, 0xC2),
        MetricKind.Vram => Color.FromRgb(0x26, 0xA6, 0x9A),
        MetricKind.Network => Color.FromRgb(0x0E, 0xA5, 0xE9),
        MetricKind.DiskActivity => Color.FromRgb(0xF5, 0x7C, 0x00),
        MetricKind.DiskSpace => Color.FromRgb(0xF5, 0x7C, 0x00),
        MetricKind.Disk => Color.FromRgb(0xF5, 0x7C, 0x00),
        MetricKind.Battery => Color.FromRgb(0x66, 0xBB, 0x6A),
        _ => Colors.Gray
    };
}

/// <summary>One reading group: icon, label, main value and an extra suffix such as temperature.</summary>
internal interface IMetricView
{
    void Update(MetricsSnapshot snapshot, AppSettings settings, Palette palette);
}

internal sealed class MetricView : StackPanel, IMetricView
{
    private readonly BlockSettings _block;
    private readonly TextBlock _label = new();
    private readonly TextBlock _value = new();
    private readonly TextBlock _extra = new();

    public MetricView(BlockSettings block, AppSettings settings, Palette palette)
    {
        _block = block;
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        Margin = new Thickness(0, 0, settings.Spacing, 0);

        if (block.ShowIcon)
        {
            var path = new Path
            {
                Data = Icons.Get(block.Kind),
                Fill = new SolidColorBrush(Icons.ColorFor(block.Kind)),
                Width = 13,
                Height = 13,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center
            };
            Children.Add(path);
        }

        foreach (var text in new[] { _label, _value, _extra })
        {
            text.FontFamily = new FontFamily(settings.FontFamily);
            text.FontSize = settings.FontSize;
            text.VerticalAlignment = VerticalAlignment.Center;
            text.SnapsToDevicePixels = true;
        }

        _label.Margin = new Thickness(4, 0, 0, 0);
        _value.Margin = new Thickness(4, 0, 0, 0);
        _extra.Margin = new Thickness(3, 0, 0, 0);

        Children.Add(_label);
        Children.Add(_value);
        Children.Add(_extra);
    }

    public void Update(MetricsSnapshot snapshot, AppSettings settings, Palette palette)
    {
        var (value, extras, percent) = Format(snapshot, settings);
        var alertPercent = _block.Kind is MetricKind.Cpu or MetricKind.Gpu or MetricKind.Ram
            ? percent
            : double.NaN;
        var alert = AlertBrush(alertPercent, settings, palette);

        _label.Text = _block.ShowLabel ? LabelText(snapshot) : "";
        _label.Visibility = _label.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _label.Foreground = palette.Label;

        var temperatureAlert = extras.Any(e => e.IsHotHot) ? palette.Critical
            : extras.Any(e => e.IsHot) ? palette.Warning
            : null;
        var hot = temperatureAlert ?? alert;

        _value.Text = value;
        _value.Foreground = hot ?? palette.Value;

        var extraText = extras.Count == 0 ? "" : "(" + string.Join(" ", extras.Select(e => e.Text)) + ")";
        _extra.Text = extraText;
        _extra.Visibility = extraText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _extra.Foreground = temperatureAlert ?? palette.Temp;
    }

    private readonly record struct Extra(string Text, bool IsHot, bool IsHotHot = false);

    /// <summary>Disk names the partition it reads, so several drives stay distinguishable.</summary>
    private string LabelText(MetricsSnapshot snapshot)
    {
        if (_block.Kind != MetricKind.Disk) return _block.Kind.DefaultLabel();
        var drive = DiskSampler.Resolve(_block.DiskDrive);
        return drive.Length >= 2
            ? $"DISK {drive[..1]}"
            : _block.Kind.DefaultLabel();
    }

    private (string value, List<Extra> extras, double percent) Format(
        MetricsSnapshot s, AppSettings settings)
    {
        var extras = new List<Extra>();
        double percent = double.NaN;
        string value;

        void AddTemperature(double celsius)
        {
            // Some laptops report 0 °C while the discrete GPU is powered down.
            if (!_block.ShowTemperature || !(celsius > 0) || celsius > 125) return;
            extras.Add(new Extra($"{celsius:0}°C", celsius >= settings.WarnTempC, celsius >= settings.CriticalTempC));
        }

        void AddClock(double megahertz)
        {
            if (!_block.ShowClock || !(megahertz > 0) || megahertz >= 15000) return;
            extras.Add(new Extra(megahertz >= 1000 ? $"{megahertz / 1000.0:0.#}GHz" : $"{megahertz:0}MHz", false));
        }

        void AddFan(double percent, double rpm)
        {
            if (!_block.ShowFan) return;
            if (!double.IsNaN(percent) && percent is >= 0 and <= 100)
                extras.Add(new Extra($"Fan {percent:0}%", false));
            else if (!double.IsNaN(rpm) && rpm >= 0)
                extras.Add(new Extra($"Fan {rpm:0}RPM", false));
        }

        switch (_block.Kind)
        {
            case MetricKind.Cpu:
                percent = s.CpuPercent;
                value = $"{s.CpuPercent:0}%";
                AddTemperature(s.CpuTemp);
                AddClock(s.CpuClock);
                break;

            case MetricKind.Gpu:
                percent = s.GpuPercent;
                value = double.IsNaN(s.GpuPercent) ? "--" : $"{s.GpuPercent:0}%";
                AddTemperature(s.GpuTemp);
                AddFan(s.GpuFan, s.GpuFanRpm);
                if (_block.ShowVram && s.VramTotal > 0)
                    extras.Add(new Extra($"VRAM {Gib(s.VramUsed):0.0}/{Gib(s.VramTotal):0.0}G", false));
                break;

            case MetricKind.Ram:
                percent = s.RamTotal > 0 ? s.RamUsed * 100.0 / s.RamTotal : double.NaN;
                value = double.IsNaN(percent) ? "--" : $"{percent:0}%";
                if (_block.ShowAmount && s.RamTotal > 0)
                    extras.Add(new Extra($"{Gib(s.RamUsed):0.0}/{Gib(s.RamTotal):0.0}G", false));
                break;

            case MetricKind.Network:
                value = $"↑ {RateNumber(s.NetUp, settings.NetUnit)}   ↓ {RateNumber(s.NetDown, settings.NetUnit)} {RateUnit(settings.NetUnit)}";
                break;

            case MetricKind.Disk:
                var drive = DiskSampler.Resolve(_block.DiskDrive);
                if (s.Disks.TryGetValue(drive, out var disk))
                {
                    percent = disk.Activity;
                    value = double.IsNaN(disk.Activity) ? "--" : $"{disk.Activity:0}%";
                    if (_block.ShowAmount && disk.Total > 0)
                        extras.Add(new Extra($"{Gib(disk.Used):0.0}/{Gib(disk.Total):0.0}G", false));
                }
                else
                {
                    value = "--";
                }
                break;

            case MetricKind.Battery:
                percent = s.BatteryPercent;
                value = s.HasBattery ? $"{s.BatteryPercent}%" : "--";
                if (s.HasBattery) extras.Add(new Extra(s.OnAc ? "AC" : "BAT", false));
                break;

            default:
                value = "--";
                break;
        }

        return (value, extras, percent);
    }

    private SolidColorBrush? AlertBrush(double percent, AppSettings settings, Palette palette)
    {
        if (double.IsNaN(percent)) return null;
        if (percent >= settings.CriticalPercent) return palette.Critical;
        if (percent >= settings.WarnPercent) return palette.Warning;
        return null;
    }

    private static double Gib(long bytes) => bytes / 1024.0 / 1024.0 / 1024.0;

    internal static string RateNumber(double bytesPerSecond, string unit)
    {
        if (!double.IsFinite(bytesPerSecond) || bytesPerSecond < 0) bytesPerSecond = 0;
        return unit == "MB/s"
            ? $"{bytesPerSecond / 1_000_000.0:0.0}"
            : $"{bytesPerSecond * 8 / 1_000_000.0:0.0}";
    }

    internal static string RateUnit(string unit) => unit == "MB/s" ? "MB/s" : "Mbps";
}

/// <summary>Network speeds as one column: upload on the top line, download below.</summary>
internal sealed class NetworkView : StackPanel, IMetricView
{
    private readonly BlockSettings _block;
    private readonly TextBlock _label = new();
    private readonly TextBlock _up = new();
    private readonly TextBlock _down = new();

    public NetworkView(BlockSettings block, AppSettings settings, Palette palette)
    {
        _block = block;
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        Margin = new Thickness(0, 0, settings.Spacing, 0);

        if (block.ShowIcon)
        {
            Children.Add(new Path
            {
                Data = Icons.Get(MetricKind.Network),
                Fill = new SolidColorBrush(Icons.ColorFor(MetricKind.Network)),
                Width = 13,
                Height = 13,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        foreach (var text in new[] { _label, _up, _down })
        {
            text.FontFamily = new FontFamily(settings.FontFamily);
            text.FontSize = settings.FontSize;
            text.SnapsToDevicePixels = true;
        }

        _label.Foreground = palette.Label;
        _label.VerticalAlignment = VerticalAlignment.Center;
        _label.Margin = new Thickness(4, 0, 4, 0);
        _label.Visibility = Visibility.Collapsed;
        Children.Add(_label);

        var lines = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        lines.Children.Add(_up);
        lines.Children.Add(_down);
        Children.Add(lines);
    }

    public void Update(MetricsSnapshot snapshot, AppSettings settings, Palette palette)
    {
        _up.Text = $"↑ {MetricView.RateNumber(snapshot.NetUp, settings.NetUnit)} {MetricView.RateUnit(settings.NetUnit)}";
        _down.Text = $"↓ {MetricView.RateNumber(snapshot.NetDown, settings.NetUnit)} {MetricView.RateUnit(settings.NetUnit)}";
        _up.Foreground = palette.Value;
        _down.Foreground = palette.Value;

        var label = _block.ShowLabel ? _block.Kind.DefaultLabel() : "";
        _label.Text = label;
        _label.Foreground = palette.Label;
        _label.Visibility = label.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}

public sealed class MeterView : UserControl
{
    // Alpha 1, so the layered window still receives clicks in the gaps between
    // readings while the taskbar shows through.
    private static readonly Brush HitTestFill = CreateHitTestFill();

    private readonly Border _panel = new();
    private readonly Grid _grid = new();
    private readonly List<IMetricView> _views = new();
    private AppSettings _settings = new();
    private Palette _palette = Palette.Create();

    // The readings are right-anchored, so every digit-width change used to shove
    // the whole block sideways. Keep the widest width seen since settings changed.
    private double _stickyWidth;

    private static Brush CreateHitTestFill()
    {
        var brush = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        brush.Freeze();
        return brush;
    }

    public MeterView()
    {
        _grid.SnapsToDevicePixels = true;
        _panel.Child = _grid;
        _panel.SnapsToDevicePixels = true;
        Content = _panel;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public void Apply(AppSettings settings, Palette palette)
    {
        _settings = settings;
        _palette = palette;
        _panel.Background = HitTestFill;
        _panel.CornerRadius = new CornerRadius(4);
        _panel.Padding = new Thickness(8, 2, 8, 2);
        _stickyWidth = 0;
        _panel.MinWidth = 0;
        Rebuild();
    }

    private void Rebuild()
    {
        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();
        _grid.RowDefinitions.Clear();
        _views.Clear();

        var rendered = PlanColumns().Where(HasVisibleView).ToList();
        if (rendered.Count == 0) return;

        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var i = 0; i < rendered.Count; i++)
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        for (var i = 0; i < rendered.Count; i++)
        {
            var column = rendered[i];
            var last = i == rendered.Count - 1;

            if (column.Tall is { } tallBlock && tallBlock.Kind == MetricKind.Network)
            {
                var view = new NetworkView(tallBlock, _settings, _palette);
                if (last) view.Margin = new Thickness(0);
                Grid.SetRow(view, 0);
                Grid.SetRowSpan(view, 2);
                Grid.SetColumn(view, i);
                _grid.Children.Add(view);
                _views.Add(view);
            }

            AddCell(column.Top, 0, i, last);
            AddCell(column.Bottom, 1, i, last);
        }

        if (_views.Count > 0) UpdateValues(App.Latest);
    }

    private void AddCell(BlockSettings? block, int row, int column, bool last)
    {
        if (block is not { Enabled: true }) return;

        var view = new MetricView(block, _settings, _palette);
        if (last) view.Margin = new Thickness(0);
        Grid.SetRow(view, row);
        Grid.SetColumn(view, column);
        _grid.Children.Add(view);
        _views.Add(view);
    }

    /// <summary>
    /// Network is a two-line column; every other reading pairs vertically in
    /// order. The Disk kind expands into one item per Disk block, so several
    /// drives can be watched at once. A disabled reading keeps its slot, so
    /// nothing jumps.
    /// </summary>
    private List<ColumnPlan> PlanColumns()
    {
        var columns = new List<ColumnPlan>();
        ColumnPlan? pair = null;

        void AddItem(BlockSettings? item)
        {
            if (item is not { Enabled: true }) return;
            pair ??= new ColumnPlan();
            if (pair.Top is null) pair.Top = item;
            else
            {
                pair.Bottom = item;
                columns.Add(pair);
                pair = null;
            }
        }

        foreach (var kind in AppSettings.LayoutOrder())
        {
            if (kind == MetricKind.Network)
            {
                var block = FindBlock(kind);
                if (block is not { Enabled: true }) continue;
                if (pair is not null)
                {
                    columns.Add(pair);
                    pair = null;
                }

                columns.Add(new ColumnPlan { Tall = block });
                continue;
            }

            if (kind == MetricKind.Disk)
                foreach (var disk in _settings.Blocks.Where(b => b.Kind == MetricKind.Disk))
                    AddItem(disk);
            else
                AddItem(FindBlock(kind));
        }

        if (pair is not null) columns.Add(pair);
        return columns;
    }

    private bool HasVisibleView(ColumnPlan column) =>
        column.Tall is { Enabled: true } || column.Top is { Enabled: true } || column.Bottom is { Enabled: true };

    private BlockSettings? FindBlock(MetricKind kind) =>
        _settings.Blocks.FirstOrDefault(b => b.Kind == kind);

    private sealed class ColumnPlan
    {
        public BlockSettings? Tall;
        public BlockSettings? Top;
        public BlockSettings? Bottom;
    }

    public void UpdateValues(MetricsSnapshot snapshot)
    {
        foreach (var view in _views) view.Update(snapshot, _settings, _palette);
        Stabilize();
    }

    /// <summary>
    /// Holds the width at its largest value since the settings last changed: it
    /// only grows when a wider reading appears (a new digit, a longer value) and
    /// never shrinks back on its own, so the block stops shoving itself sideways
    /// every second. Text keeps its natural left alignment — no reserved slots.
    /// </summary>
    private void Stabilize()
    {
        var natural = _grid.DesiredSize.Width;
        if (!(natural > 0)) return;

        var wanted = natural + _panel.Padding.Left + _panel.Padding.Right;
        if (wanted <= _stickyWidth + 0.5) return;

        _stickyWidth = wanted;
        _panel.MinWidth = wanted;
    }

    public void ApplyStyle(AppSettings settings, Palette palette)
    {
        _settings = settings;
        _palette = palette;
        _panel.Background = HitTestFill;
        foreach (var view in _views) view.Update(App.Latest, _settings, _palette);
    }
}
