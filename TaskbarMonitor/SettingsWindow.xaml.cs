using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;
using WinForms = System.Windows.Forms;

namespace TaskbarMonitor;

public partial class SettingsWindow : Window
{
    private readonly MeterView _preview = new();
    private readonly DispatcherTimer _statusTimer;
    private BlockSettings? _selectedBlock;
    private bool _loading;
    private bool? _chromeDark;

    private const string PawnIoUrl =
        "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe";

    private sealed record MonitorEntry(string Device, string Display)
    {
        public override string ToString() => Display;
    }

    private sealed record DriveEntry(string Letter, string Display)
    {
        public override string ToString() => Display;
    }

    public SettingsWindow()
    {
        InitializeComponent();

        FooterText.Text = "Changes apply instantly and are saved automatically.  ·  v" + AppVersion.Display;

        PreviewMeterHost.Child = _preview;
        BuildChoices();
        LoadFromSettings();
        HookEvents();
        ApplyChrome();

        UpdatePreview(true);
        UpdateStatus();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();
        Closed += (_, _) => _statusTimer.Stop();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Dark title bar to sit next to the dark sidebar. Windows 10 cannot flip
        // it back at runtime, so it stays dark in both themes.
        var handle = new WindowInteropHelper(this).Handle;
        var dark = 1;
        if (Native.DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
            Native.DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
    }

    /// <summary>Re-resolves the Auto theme after the Windows light/dark setting changes.</summary>
    public void RefreshTheme() => ApplyChrome();

    /// <summary>Colors of this window; the taskbar overlay ignores Settings.Theme.</summary>
    private void ApplyChrome()
    {
        ApplyPreviewChrome();

        var dark = App.Settings.Theme switch
        {
            "Light" => false,
            "Dark" => true,
            _ => !Palette.SystemAppsUseLightTheme()
        };

        if (_chromeDark == dark) return;
        _chromeDark = dark;

        foreach (var (key, color) in Palette.AppColors(dark))
            Resources[key] = new SolidColorBrush(color);

        RebuildBlocks();
    }

    /// <summary>
    /// The preview simulates the taskbar, so its strip and clock must match the
    /// overlay palette instead of the settings-window theme.
    /// </summary>
    private void ApplyPreviewChrome()
    {
        var palette = App.CurrentPalette;
        var light = palette.Light;

        PreviewBar.Background = new SolidColorBrush(Palette.PreviewBarBackground(light));
        PreviewBar.BorderBrush = new SolidColorBrush(light
            ? Color.FromRgb(0xE0, 0xE3, 0xE9)
            : Color.FromRgb(0x2B, 0x2E, 0x36));
        PreviewClock.Foreground = palette.Value;
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (BlocksPage is null) return;

        var tag = (sender as FrameworkElement)?.Tag as string ?? "Blocks";

        BlocksPage.Visibility = tag == "Blocks" ? Visibility.Visible : Visibility.Collapsed;
        AppearancePage.Visibility = tag == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        PositionPage.Visibility = tag == "Position" ? Visibility.Visible : Visibility.Collapsed;
        StartupPage.Visibility = tag == "Startup" ? Visibility.Visible : Visibility.Collapsed;

        PageTitle.Text = tag == "Blocks" ? "Widgets" : tag;
        PageSubtitle.Text = tag switch
        {
            "Appearance" => "Theme of this window, typography and units.",
            "Position" => "Where the readings sit on the taskbar.",
            "Startup" => "Startup, permissions and sensor status.",
            _ => "Choose which widgets appear on the taskbar."
        };
    }

    private void BuildChoices()
    {
        ThemeCombo.ItemsSource = new[] { "Auto", "Light", "Dark" };
        FontCombo.ItemsSource = new[] { "Segoe UI", "Segoe UI Variable Text", "Tahoma", "Consolas", "Bahnschrift", "Arial" };
        NetUnitCombo.ItemsSource = new[] { "Mbps", "MB/s" };
    }

    private void HookEvents()
    {
        ThemeCombo.SelectionChanged += (_, _) => Apply();
        FontCombo.SelectionChanged += (_, _) => Apply();

        SizeSlider.ValueChanged += (_, _) => { SizeValue.Text = $"{SizeSlider.Value:0}"; Apply(); };
        SpacingSlider.ValueChanged += (_, _) => { SpacingValue.Text = $"{SpacingSlider.Value:0}"; Apply(); };

        NetUnitCombo.SelectionChanged += (_, _) => Apply();

        AllMonitorsCheck.Checked += (_, _) => { MonitorCombo.IsEnabled = false; Apply(rebuildOverlays: true); };
        AllMonitorsCheck.Unchecked += (_, _) => { MonitorCombo.IsEnabled = true; Apply(rebuildOverlays: true); };
        FullscreenCheck.Checked += (_, _) => Apply();
        FullscreenCheck.Unchecked += (_, _) => Apply();
        MonitorCombo.SelectionChanged += (_, _) => Apply(rebuildOverlays: true);

        GapSlider.ValueChanged += (_, _) => Apply();
        RightOffsetSlider.ValueChanged += (_, _) => Apply();
        VerticalSlider.ValueChanged += (_, _) => Apply();
        BindNumber(GapSlider, GapValue);
        BindNumber(RightOffsetSlider, RightOffsetValue);
        BindNumber(VerticalSlider, VerticalValue);

        RefreshSlider.ValueChanged += (_, _) => Apply();
        PercentAmberSlider.ValueChanged += (_, _) => Apply();
        PercentRedSlider.ValueChanged += (_, _) => Apply();
        TempAmberSlider.ValueChanged += (_, _) => Apply();
        TempRedSlider.ValueChanged += (_, _) => Apply();
        AlertsCheck.Checked += (_, _) => Apply();
        AlertsCheck.Unchecked += (_, _) => Apply();
        BindNumber(RefreshSlider, RefreshValue);
        BindNumber(PercentAmberSlider, PercentAmberValue);
        BindNumber(PercentRedSlider, PercentRedValue);
        BindNumber(TempAmberSlider, TempAmberValue);
        BindNumber(TempRedSlider, TempRedValue);

        StartWithWindowsCheck.Checked += (_, _) => StartupChanged();
        StartWithWindowsCheck.Unchecked += (_, _) => StartupChanged();
        StartElevatedCheck.Checked += (_, _) => StartupChanged();
        StartElevatedCheck.Unchecked += (_, _) => StartupChanged();
    }

    private void LoadFromSettings()
    {
        _loading = true;

        var settings = App.Settings;
        var selectedKind = _selectedBlock?.Kind;

        ThemeCombo.SelectedItem = settings.Theme;
        FontCombo.SelectedItem = settings.FontFamily;
        SizeSlider.Value = settings.FontSize;
        SpacingSlider.Value = settings.Spacing;
        NetUnitCombo.SelectedItem = settings.NetUnit;
        AllMonitorsCheck.IsChecked = settings.ShowOnAllMonitors;
        FullscreenCheck.IsChecked = settings.ShowInFullscreen;
        GapSlider.Value = settings.GapFromTray;
        RightOffsetSlider.Value = settings.OffsetFromRight;
        VerticalSlider.Value = settings.OffsetY;
        RefreshSlider.Value = Math.Clamp(settings.RefreshMs, 250, 10000);
        PercentAmberSlider.Value = settings.WarnPercent;
        PercentRedSlider.Value = settings.CriticalPercent;
        TempAmberSlider.Value = settings.WarnTempC;
        TempRedSlider.Value = settings.CriticalTempC;
        AlertsCheck.IsChecked = settings.AlertsEnabled;
        StartWithWindowsCheck.IsChecked = settings.StartWithWindows;
        StartElevatedCheck.IsChecked = settings.StartElevated;

        SizeValue.Text = $"{settings.FontSize:0}";
        SpacingValue.Text = $"{settings.Spacing:0}";
        GapValue.Text = $"{settings.GapFromTray:0}";
        RightOffsetValue.Text = $"{settings.OffsetFromRight:0}";
        VerticalValue.Text = $"{settings.OffsetY:0}";

        PopulateMonitors(settings);

        _selectedBlock = selectedKind is null
            ? null
            : settings.Blocks.FirstOrDefault(b => b.Kind == selectedKind);

        RebuildBlocks();

        _loading = false;
    }

    private void PopulateMonitors(AppSettings settings)
    {
        var screens = WinForms.Screen.AllScreens;
        var entries = screens
            .Select(screen => new MonitorEntry(screen.DeviceName,
                $"{screen.DeviceName}  —  {screen.Bounds.Width}×{screen.Bounds.Height}{(screen.Primary ? "  (primary)" : "")}"))
            .ToList();

        MonitorCombo.ItemsSource = entries;
        var primary = screens.FirstOrDefault(s => s.Primary)?.DeviceName;
        MonitorCombo.SelectedItem =
            entries.FirstOrDefault(e => string.Equals(e.Device, settings.MonitorDevice, StringComparison.OrdinalIgnoreCase))
            ?? entries.FirstOrDefault(e => e.Device == primary)
            ?? entries.FirstOrDefault();
        MonitorCombo.IsEnabled = !settings.ShowOnAllMonitors;
    }

    private void RebuildBlocks()
    {
        BlocksPanel.Children.Clear();

        var blocks = App.Settings.Blocks.OrderBy(b => (int)b.Kind).ToList();
        for (var i = 0; i < blocks.Count; i++)
        {
            if (i > 0)
            {
                BlocksPanel.Children.Add(new Border
                {
                    Height = 1,
                    Background = (Brush)FindResource("LineSoft"),
                    Margin = new Thickness(16, 0, 16, 0)
                });
            }

            BlocksPanel.Children.Add(BuildBlockRow(blocks[i]));
        }

        WidgetsCount.Text = $"{blocks.Count(b => b.Enabled)} of {blocks.Count} on";
    }

    private FrameworkElement BuildBlockRow(BlockSettings block)
    {
        var expanded = ReferenceEquals(block, _selectedBlock);
        var muted = (Brush)FindResource("Muted");
        var batteryMissing = block.Kind == MetricKind.Battery && !BatterySampler.IsPresent();

        var header = new Grid { Height = 54, Margin = new Thickness(16, 0, 12, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Path
        {
            Data = Icons.Get(block.Kind),
            Fill = new SolidColorBrush(Icons.ColorFor(block.Kind)),
            Width = 15,
            Height = 15,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center
        };

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
        text.Children.Add(new TextBlock { Text = block.Kind.Title(), FontSize = 13, Foreground = (Brush)FindResource("Ink") });
        text.Children.Add(new TextBlock
        {
            Text = batteryMissing
                ? block.Kind.Description() + "  ·  No battery detected"
                : block.Kind.Description(),
            FontSize = 11.5,
            Foreground = muted,
            Margin = new Thickness(0, 2, 0, 0)
        });

        var toggle = new CheckBox
        {
            Style = (Style)FindResource("Switch"),
            IsChecked = block.Enabled,
            IsEnabled = !batteryMissing,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = batteryMissing
                ? "No battery detected on this PC"
                : "Show this block on the taskbar"
        };
        toggle.Checked += (_, _) => { block.Enabled = true; Apply(); RebuildBlocks(); };
        toggle.Unchecked += (_, _) => { block.Enabled = false; Apply(); RebuildBlocks(); };

        var chevron = new Path
        {
            Data = Geometry.Parse(expanded ? "M2,7 L6,3 L10,7" : "M2,4 L6,8 L10,4"),
            Stroke = muted,
            StrokeThickness = 1.6,
            Width = 12,
            Height = 12,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 2, 0)
        };

        Grid.SetColumn(text, 1);
        Grid.SetColumn(toggle, 2);
        Grid.SetColumn(chevron, 3);
        header.Children.Add(icon);
        header.Children.Add(text);
        header.Children.Add(toggle);
        header.Children.Add(chevron);

        header.MouseLeftButtonUp += (_, _) =>
        {
            _selectedBlock = expanded ? null : block;
            RebuildBlocks();
        };

        var content = new StackPanel();
        content.Children.Add(header);

        if (expanded)
        {
            content.Children.Add(new Border
            {
                Height = 1,
                Background = (Brush)FindResource("LineSoft"),
                Margin = new Thickness(16, 0, 16, 0)
            });
            content.Children.Add(BuildOptions(block));
        }

        return new Border
        {
            Child = content,
            Background = expanded ? (Brush)FindResource("RowHighlight") : Brushes.Transparent,
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(4, 2, 4, 2)
        };
    }

    private FrameworkElement BuildOptions(BlockSettings block)
    {
        var panel = new StackPanel { Margin = new Thickness(16, 12, 16, 14) };

        var toggles = new WrapPanel();
        toggles.Children.Add(OptionChip("Icon", block.ShowIcon, v => { block.ShowIcon = v; Apply(); }));
        toggles.Children.Add(OptionChip("Name", block.ShowLabel, v => { block.ShowLabel = v; Apply(); }));
        if (block.Kind.SupportsAmount())
            toggles.Children.Add(OptionChip("Used / total", block.ShowAmount, v => { block.ShowAmount = v; Apply(); }));
        if (block.Kind.SupportsTemperature())
            toggles.Children.Add(OptionChip("Temperature", block.ShowTemperature,
                v => SetAdminOption(block, v, x => block.ShowTemperature = x), NeedsAdmin(block.Kind)));
        if (block.Kind.SupportsClock())
            toggles.Children.Add(OptionChip("Frequency", block.ShowClock,
                v => SetAdminOption(block, v, x => block.ShowClock = x), NeedsAdmin(block.Kind)));
        if (block.Kind == MetricKind.Gpu)
            toggles.Children.Add(OptionChip("VRAM", block.ShowVram, v => { block.ShowVram = v; Apply(); }));
        if (block.Kind == MetricKind.Gpu)
            toggles.Children.Add(OptionChip("Fan", block.ShowFan, v => { block.ShowFan = v; Apply(); }));
        panel.Children.Add(toggles);

        if (block.Kind == MetricKind.Disk)
            panel.Children.Add(BuildDrivePicker(block));
        if (block.Kind == MetricKind.Network) panel.Children.Add(BuildAdapterPicker());

        return panel;
    }


    /// <summary>Fixed drives as "C:  476 GB   (Windows)"; the default is the Windows drive.</summary>
    private FrameworkElement BuildDrivePicker(BlockSettings block)
    {
        var system = DiskSampler.SystemDrive();
        var entries = new List<DriveEntry>();

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;

                var letter = drive.Name.TrimEnd('\\').ToUpperInvariant();
                var size = drive.TotalSize >= 1L << 30
                    ? $"{drive.TotalSize / (double)(1L << 30):0.#} GB"
                    : $"{drive.TotalSize / (double)(1L << 20):0} MB";
                var windows = string.Equals(letter, system, StringComparison.OrdinalIgnoreCase)
                    ? "   (Windows)"
                    : "";
                entries.Add(new DriveEntry(letter, $"{letter}   {size}{windows}"));
            }
        }
        catch { /* drives can disappear mid-enumeration */ }

        var selected = AppSettings.NormalizeDrive(block.DiskDrive);
        if (selected.Length == 0) selected = system;
        if (selected.Length > 0 && entries.All(e => e.Letter != selected))
            entries.Add(new DriveEntry(selected, $"{selected}   (not connected)"));

        var combo = new ComboBox
        {
            Width = 300,
            ItemsSource = entries,
            SelectedItem = entries.FirstOrDefault(e => e.Letter == selected) ?? entries.FirstOrDefault(),
            VerticalAlignment = VerticalAlignment.Center
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not DriveEntry entry) return;
            block.DiskDrive = entry.Letter;
            Apply();
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(new TextBlock
        {
            Text = "Drive",
            Style = (Style)FindResource("RowDesc"),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 44,
            Margin = new Thickness(0, 0, 8, 0)
        });
        row.Children.Add(combo);
        return row;
    }

    /// <summary>Adapters as "Ethernet · Realtek PCIe GbE · 1 Gbps"; the default measures every active adapter.</summary>
    private FrameworkElement BuildAdapterPicker()
    {
        var entries = new List<AdapterEntry> { new("", "All active adapters") };

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

                var speed = nic.Speed >= 1_000_000_000
                    ? $"{nic.Speed / 1_000_000_000.0:0.#} Gbps"
                    : nic.Speed > 0 ? $"{nic.Speed / 1_000_000.0:0} Mbps" : "unplugged";

                var label = $"{nic.Name}  ·  {nic.Description}  ·  {speed}";
                if (label.Length > 68) label = label[..65].TrimEnd() + "…";
                entries.Add(new AdapterEntry(nic.Id, label));
            }
        }
        catch { /* adapters can disappear mid-enumeration */ }

        var selected = App.Settings.NetAdapter;
        if (selected.Length > 0 && entries.All(e => e.Id != selected))
            entries.Add(new AdapterEntry(selected, selected + "   (not connected)"));

        var combo = new ComboBox
        {
            Width = 300,
            ItemsSource = entries,
            SelectedItem = entries.FirstOrDefault(e => e.Id == selected) ?? entries[0],
            VerticalAlignment = VerticalAlignment.Center
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not AdapterEntry entry) return;
            App.Settings.NetAdapter = entry.Id;
            Apply();
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(new TextBlock
        {
            Text = "Adapter",
            Style = (Style)FindResource("RowDesc"),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 44,
            Margin = new Thickness(0, 0, 8, 0)
        });
        row.Children.Add(combo);
        return row;
    }

    private sealed record AdapterEntry(string Id, string Label);

    /// <summary>CPU temperature comes from LibreHardwareMonitor, which needs admin.</summary>
    private static bool NeedsAdmin(MetricKind kind) => kind == MetricKind.Cpu;

    private void SetAdminOption(BlockSettings block, bool on, Action<bool> assign)
    {
        assign(on);
        Apply();

        if (!on || App.IsElevated || !NeedsAdmin(block.Kind)) return;

        var answer = MessageBox.Show(this,
            "CPU temperature needs administrator access.\n\nRestart Taskbar Monitor as administrator now?",
            "Taskbar Monitor", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer == MessageBoxResult.Yes)
        {
            App.RestartElevated();
            return;
        }

        assign(false);
        Apply();
        RebuildBlocks();
    }

    private FrameworkElement OptionChip(string text, bool value, Action<bool> changed, bool adminOnly = false)
    {
        var box = new CheckBox
        {
            Style = (Style)FindResource("Switch"),
            IsChecked = value,
            VerticalAlignment = VerticalAlignment.Center
        };
        box.Checked += (_, _) => changed(true);
        box.Unchecked += (_, _) => changed(false);

        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        content.Children.Add(box);

        return new Border
        {
            Child = content,
            Background = (Brush)FindResource("Chip"),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 8),
            ToolTip = adminOnly && !App.IsElevated ? "Needs administrator" : null
        };
    }

    private void ReadControls()
    {
        var settings = App.Settings;
        settings.Theme = ThemeCombo.SelectedItem as string ?? "Auto";
        settings.FontFamily = FontCombo.SelectedItem as string ?? "Segoe UI";
        settings.FontSize = Math.Round(SizeSlider.Value);
        settings.Spacing = Math.Round(SpacingSlider.Value);
        settings.NetUnit = NetUnitCombo.SelectedItem as string ?? "Mbps";
        settings.ShowOnAllMonitors = AllMonitorsCheck.IsChecked == true;
        settings.ShowInFullscreen = FullscreenCheck.IsChecked == true;
        if (MonitorCombo.SelectedItem is MonitorEntry entry) settings.MonitorDevice = entry.Device;
        settings.GapFromTray = Math.Round(GapSlider.Value);
        settings.OffsetFromRight = Math.Round(RightOffsetSlider.Value);
        settings.OffsetY = Math.Round(VerticalSlider.Value);
        settings.RefreshMs = (int)Math.Round(RefreshSlider.Value);
        settings.WarnPercent = Math.Round(PercentAmberSlider.Value);
        settings.CriticalPercent = Math.Round(PercentRedSlider.Value);
        settings.WarnTempC = Math.Round(TempAmberSlider.Value);
        settings.CriticalTempC = Math.Round(TempRedSlider.Value);
        settings.AlertsEnabled = AlertsCheck.IsChecked == true;
        settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        settings.StartElevated = StartElevatedCheck.IsChecked == true;
    }

    private void Apply(bool rebuildOverlays = false)
    {
        if (_loading) return;

        ReadControls();
        ApplyChrome();
        App.ApplySettings();
        if (rebuildOverlays) App.RebuildOverlays();
    }

    public void UpdatePreview(bool rebuild)
    {
        if (rebuild) _preview.Apply(App.Settings, App.CurrentPalette);
        else _preview.ApplyStyle(App.Settings, App.CurrentPalette);
    }

    /// <summary>Two-way link between a slider and its box, so exact pixels can be typed.</summary>
    private void BindNumber(Slider slider, TextBox box)
    {
        var syncing = false;

        slider.ValueChanged += (_, _) =>
        {
            if (syncing) return;
            syncing = true;
            box.Text = $"{slider.Value:0}";
            syncing = false;
        };

        box.TextChanged += (_, _) =>
        {
            if (syncing || _loading) return;
            if (!double.TryParse(box.Text, out var value)) return;

            syncing = true;
            slider.Value = Math.Round(value);
            syncing = false;
        };

        box.LostFocus += (_, _) => box.Text = $"{slider.Value:0}";
    }

    private void ResetPlacement_Click(object sender, RoutedEventArgs e)
    {
        GapSlider.Value = 8;
        RightOffsetSlider.Value = 0;
        VerticalSlider.Value = 0;
    }

    private void StartupChanged()
    {
        if (_loading) return;

        App.Settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        App.Settings.StartElevated = StartElevatedCheck.IsChecked == true;

        if (App.Settings.StartWithWindows && App.Settings.StartElevated && !App.IsElevated)
        {
            var answer = MessageBox.Show(this,
                "Elevated startup has to be registered by an administrator once.\n\nRestart Taskbar Monitor as administrator now?",
                "Taskbar Monitor", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (answer == MessageBoxResult.Yes)
            {
                App.RestartElevated();
                return;
            }

            _loading = true;
            StartElevatedCheck.IsChecked = false;
            _loading = false;
            App.Settings.StartElevated = false;
        }

        Startup.Apply(App.Settings.StartWithWindows, App.Settings.StartElevated);
        UpdateStatus();
        App.QueueSave();
    }

    private void UpdateStatus()
    {
        ElevationBadge.Text = App.IsElevated ? "Administrator" : "Standard user";

        CpuStatusText.Text = App.Metrics.CpuTemps.InitializationFinished
            ? "CPU temperature — " + App.Metrics.CpuTemps.Status
            : "CPU temperature — reading sensors…";

        var temps = App.Metrics.CpuTemps;
        InstallPawnIoButton.Visibility = temps.InitializationFinished && !temps.Available && !PawnIoInstalled()
            ? Visibility.Visible
            : Visibility.Collapsed;

        GpuStatusText.Text = App.Metrics.GpuStatus;

        StartupStatusText.Text = App.Settings.StartWithWindows
            ? App.Settings.StartElevated
                ? "Startup — scheduled task with highest privileges (no UAC prompt at logon)."
                : "Startup — current-user Run key."
            : "Startup — disabled.";
    }

    private void RestartAdmin_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Restart Taskbar Monitor as administrator? The current window will close.",
            "Taskbar Monitor", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer == MessageBoxResult.Yes) App.RestartElevated();
    }

    /// <summary>LibreHardwareMonitor 0.9.6 reads the CPU through PawnIO, which ships separately.</summary>
    private static bool PawnIoInstalled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
            return key is not null;
        }
        catch { return false; }
    }

    private async void InstallPawnIo_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var label = button.Content;
        button.IsEnabled = false;
        button.Content = "Downloading…";

        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PawnIO_setup.exe");
        try
        {
            using var http = new HttpClient();
            File.WriteAllBytes(file, await http.GetByteArrayAsync(PawnIoUrl));

            button.Content = "Installing…";
            using var setup = Process.Start(new ProcessStartInfo(file, "-install")
            {
                UseShellExecute = true,
                Verb = "runas"
            });
            if (setup is not null) await setup.WaitForExitAsync();

            if (!PawnIoInstalled())
            {
                MessageBox.Show(this, "PawnIO is not installed.", "Taskbar Monitor",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var answer = MessageBox.Show(this,
                "PawnIO is installed. Restart Taskbar Monitor as administrator to read the CPU temperature now?",
                "Taskbar Monitor", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes) App.RestartElevated();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            /* UAC prompt declined */
        }
        catch (Exception ex)
        {
            Log.Error("install pawnio", ex);
            MessageBox.Show(this, "Could not install PawnIO: " + ex.Message, "Taskbar Monitor",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            button.Content = label;
            button.IsEnabled = true;
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            Process.Start(new ProcessStartInfo(SettingsStore.Folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("open settings folder", ex);
        }
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, "Reset every setting to its default?",
            "Taskbar Monitor", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        App.ResetSettings();
        _selectedBlock = null;
        LoadFromSettings();
        UpdatePreview(true);
        UpdateStatus();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
