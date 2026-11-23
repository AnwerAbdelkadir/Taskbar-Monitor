using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarMonitor;

public enum MetricKind
{
    Network,
    Cpu,
    Gpu,
    Ram,
    Disk,
    Battery,

    // Legacy values kept only so older settings files still deserialize.
    Vram,
    DiskActivity,
    DiskSpace,
    NetUp,
    NetDown
}

public sealed class BlockSettings
{
    public MetricKind Kind { get; set; }
    public bool Enabled { get; set; } = true;
    public bool ShowLabel { get; set; } = true;
    public bool ShowIcon { get; set; } = true;
    public bool ShowTemperature { get; set; } = true;
    public bool ShowVram { get; set; } = true;
    public bool ShowAmount { get; set; } = true;
    public bool ShowPower { get; set; }

    public BlockSettings Clone() => (BlockSettings)MemberwiseClone();
}

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public int RefreshMs { get; set; } = 1000;

    public string Theme { get; set; } = "Auto";          // Auto | Light | Dark (settings window only)
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 12;
    public double Spacing { get; set; } = 12;

    public double OffsetFromRight { get; set; } = 0;      // 0 = auto (left of tray)
    public double GapFromTray { get; set; } = 8;
    public double OffsetY { get; set; } = 0;
    public bool ShowOnAllMonitors { get; set; }
    public string MonitorDevice { get; set; } = "";
    public bool ShowInFullscreen { get; set; }             // keep drawing over full-screen apps

    public bool StartWithWindows { get; set; }
    public bool StartElevated { get; set; }
    public string NetUnit { get; set; } = "Mbps";         // Mbps | MB/s

    public List<BlockSettings> Blocks { get; set; } = DefaultBlocks();

    /// <summary>Fixed taskbar layout: Network is a 2-row column, the rest pair up.</summary>
    public static List<MetricKind> LayoutOrder() => new()
    {
        MetricKind.Network,
        MetricKind.Cpu,
        MetricKind.Gpu,
        MetricKind.Ram,
        MetricKind.Disk,
        MetricKind.Battery
    };

    public static List<BlockSettings> DefaultBlocks() => new()
    {
        new() { Kind = MetricKind.Network, ShowLabel = false },
        new() { Kind = MetricKind.Cpu },
        new() { Kind = MetricKind.Gpu },
        new() { Kind = MetricKind.Ram, ShowTemperature = false },
        new() { Kind = MetricKind.Disk, Enabled = false, ShowTemperature = false },
        new() { Kind = MetricKind.Battery, Enabled = false, ShowTemperature = false }
    };

    public AppSettings Clone()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.Blocks = Blocks.Select(b => b.Clone()).ToList();
        return copy;
    }

    public void Normalize()
    {
        Blocks ??= DefaultBlocks();

        // Fold the older split widgets into the merged ones. Enabled wins.
        MergeLegacy(MetricKind.Network, MetricKind.NetUp, MetricKind.NetDown);
        MergeLegacy(MetricKind.Gpu, MetricKind.Vram);
        MergeLegacy(MetricKind.Disk, MetricKind.DiskActivity, MetricKind.DiskSpace);

        var supported = LayoutOrder();
        foreach (var kind in supported)
            if (Blocks.All(b => b.Kind != kind))
                Blocks.Add(new BlockSettings { Kind = kind, Enabled = false });

        Blocks.RemoveAll(b =>
            b.Kind is MetricKind.NetUp or MetricKind.NetDown or MetricKind.Vram
                or MetricKind.DiskActivity or MetricKind.DiskSpace ||
            !Enum.IsDefined(b.Kind));

        if (!Blocks.Any(b => b.Enabled)) Blocks[0].Enabled = true;

        Blocks.Sort((a, b) => ((int)a.Kind).CompareTo((int)b.Kind));

        RefreshMs = Math.Clamp(RefreshMs, 250, 10000);
        FontSize = Math.Clamp(FontSize, 8, 24);
        Spacing = Math.Clamp(Spacing, 0, 40);
        GapFromTray = Math.Clamp(GapFromTray, 0, 80);
        OffsetFromRight = Math.Clamp(OffsetFromRight, 0, 900);
        OffsetY = Math.Clamp(OffsetY, -30, 40);
        if (string.IsNullOrWhiteSpace(FontFamily)) FontFamily = "Segoe UI";
        if (NetUnit != "MB/s") NetUnit = "Mbps";
        if (Theme is not ("Auto" or "Light" or "Dark")) Theme = "Auto";
    }

    private void MergeLegacy(MetricKind target, params MetricKind[] legacy)
    {
        var sources = Blocks.Where(b => legacy.Contains(b.Kind)).ToList();
        if (sources.Count == 0) return;

        var enabled = sources.Any(b => b.Enabled);
        Blocks.RemoveAll(b => legacy.Contains(b.Kind));

        var block = Blocks.FirstOrDefault(b => b.Kind == target);
        if (block is null) Blocks.Add(new BlockSettings { Kind = target, Enabled = enabled });
        else block.Enabled |= enabled;
    }
}

public static class MetricKindInfo
{
    public static string Title(this MetricKind k) => k switch
    {
        MetricKind.Network or MetricKind.NetUp or MetricKind.NetDown => "Network speed (upload & download)",
        MetricKind.Cpu => "CPU usage & temperature",
        MetricKind.Gpu or MetricKind.Vram => "GPU usage, VRAM, temperature & power",
        MetricKind.Ram => "RAM used / total",
        MetricKind.Disk or MetricKind.DiskActivity or MetricKind.DiskSpace => "Disk activity & space",
        MetricKind.Battery => "Battery level",
        _ => k.ToString()
    };

    public static string ShortTitle(this MetricKind k) => k switch
    {
        MetricKind.Network => "Network",
        MetricKind.Cpu => "CPU",
        MetricKind.Gpu or MetricKind.Vram => "GPU",
        MetricKind.Ram => "RAM",
        MetricKind.Disk or MetricKind.DiskActivity or MetricKind.DiskSpace => "Disk",
        MetricKind.Battery => "Battery",
        _ => k.Title()
    };

    public static string DefaultLabel(this MetricKind k) => k switch
    {
        MetricKind.Cpu => "CPU",
        MetricKind.Gpu or MetricKind.Vram => "GPU",
        MetricKind.Ram => "RAM",
        MetricKind.Disk or MetricKind.DiskActivity or MetricKind.DiskSpace => "DISK",
        MetricKind.Battery => "BAT",
        _ => ""
    };

    public static string Description(this MetricKind k) => k switch
    {
        MetricKind.Network or MetricKind.NetUp or MetricKind.NetDown => "Upload and download of active adapters",
        MetricKind.Cpu => "Usage, temperature and package power",
        MetricKind.Gpu or MetricKind.Vram => "Usage, VRAM, temperature and power draw",
        MetricKind.Ram => "Memory in use out of total",
        MetricKind.Disk or MetricKind.DiskActivity or MetricKind.DiskSpace => "Activity and used space of fixed drives",
        MetricKind.Battery => "Charge level and power source",
        _ => ""
    };

    public static bool SupportsAmount(this MetricKind k) => k is MetricKind.Ram or MetricKind.Disk;

    public static bool SupportsTemperature(this MetricKind k) => k is MetricKind.Cpu or MetricKind.Gpu;

    public static bool SupportsPower(this MetricKind k) => k is MetricKind.Cpu or MetricKind.Gpu;
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarMonitor");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
                if (settings != null)
                {
                    settings.Normalize();
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("settings load", ex);
        }

        var defaults = new AppSettings();
        defaults.Normalize();
        return defaults;
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Error("settings save", ex);
        }
    }
}

public static class Log
{
    private static readonly object Gate = new();
    private static string FilePath => Path.Combine(SettingsStore.Folder, "log.txt");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string where, Exception ex) => Write("ERROR", where, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        // shortcut: one log file, no rotation; fine for a personal tray tool.
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(SettingsStore.Folder);
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{(ex is null ? "" : " :: " + ex)}";
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // logging must never take the app down
        }
    }
}
