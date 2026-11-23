using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace TaskbarMonitor;

public sealed class MetricsSnapshot
{
    public double CpuPercent;
    public double CpuTemp = double.NaN;
    public double CpuClock = double.NaN;      // MHz, fastest core

    public long RamUsed;
    public long RamTotal;

    public double GpuPercent = double.NaN;
    public double GpuTemp = double.NaN;
    public double GpuFan = double.NaN;        // percent of the first fan
    public long VramUsed;
    public long VramTotal;

    public double NetUp;      // bytes / second
    public double NetDown;    // bytes / second

    public double DiskActivity = double.NaN;
    public long DiskUsed;
    public long DiskTotal;
    public string DiskDrive = "";

    public bool HasBattery;
    public int BatteryPercent = -1;
    public bool OnAc;
}

internal sealed class CpuSampler
{
    private ulong _idle, _kernel, _user;

    public double Sample()
    {
        if (!Native.GetSystemTimes(out var idle, out var kernel, out var user))
            return 0;

        var i = idle.ToUInt64;
        var k = kernel.ToUInt64;
        var u = user.ToUInt64;

        if (_kernel == 0 && _user == 0)
        {
            (_idle, _kernel, _user) = (i, k, u);
            return 0;
        }

        var idleDelta = i - _idle;
        var total = (k - _kernel) + (u - _user);
        (_idle, _kernel, _user) = (i, k, u);

        return total == 0 ? 0 : Math.Clamp((total - idleDelta) * 100.0 / total, 0, 100);
    }
}

internal static class MemorySampler
{
    public static (long used, long total) Sample()
    {
        var status = new MEMORYSTATUSEX { Length = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!Native.GlobalMemoryStatusEx(ref status)) return (0, 0);
        return ((long)(status.TotalPhys - status.AvailPhys), (long)status.TotalPhys);
    }
}

internal sealed class NetworkSampler
{
    private long _rx, _tx;
    private long _timestamp;
    private string? _adapterId;   // null = every active adapter

    /// <summary>An explicit adapter wins over the "active adapters" heuristics,
    /// so a VPN or virtual NIC can be watched on purpose.</summary>
    public void SetAdapter(string? id) => _adapterId = string.IsNullOrWhiteSpace(id) ? null : id;

    public (double up, double down) Sample()
    {
        long rx = 0, tx = 0;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (_adapterId is not null)
                {
                    if (nic.Id != _adapterId) continue;
                }
                else
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                    if (nic.Speed <= 0) continue;

                    var description = nic.Description;
                    if (description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                        description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) ||
                        description.Contains("WAN Miniport", StringComparison.OrdinalIgnoreCase) ||
                        description.Contains("TAP-Windows", StringComparison.OrdinalIgnoreCase) ||
                        description.Contains("WireGuard", StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                var stats = nic.GetIPStatistics();
                rx += stats.BytesReceived;
                tx += stats.BytesSent;
            }
        }
        catch (Exception ex)
        {
            Log.Error("network sample", ex);
        }

        var now = Stopwatch.GetTimestamp();
        var elapsed = _timestamp == 0 ? 0 : (now - _timestamp) / (double)Stopwatch.Frequency;
        _timestamp = now;

        if (elapsed < 0.1)
        {
            _rx = rx;
            _tx = tx;
            return (0, 0);
        }

        var down = Math.Max(0, rx - _rx) / elapsed;
        var up = Math.Max(0, tx - _tx) / elapsed;
        _rx = rx;
        _tx = tx;
        return (up, down);
    }
}

internal sealed class DiskSampler : IDisposable
{
    private IntPtr _query;
    private IntPtr _counter;
    private bool _ready;
    private string _drive;

    public DiskSampler()
    {
        _drive = SystemDrive();
        Rebuild(_drive);
    }

    /// <summary>The drive Windows is installed on, for example "C:".</summary>
    public static string SystemDrive()
    {
        try
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var root = Path.GetPathRoot(windows);
            return root is { Length: >= 2 } ? root[..2].ToUpperInvariant() : "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// The configured drive when it is a mounted fixed disk, otherwise the Windows
    /// drive, otherwise empty (no fixed drive at all).
    /// </summary>
    public static string Resolve(string? drive)
    {
        var wanted = AppSettings.NormalizeDrive(drive);
        if (wanted.Length > 0 && IsFixed(wanted)) return wanted;

        var system = SystemDrive();
        return system.Length > 0 && IsFixed(system) ? system : "";
    }

    public static bool IsFixed(string drive)
    {
        try
        {
            var info = new DriveInfo(drive);
            return info.IsReady && info.DriveType == DriveType.Fixed;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Points the activity counter at the drive; no-op while it is unchanged.</summary>
    public void SetDrive(string drive)
    {
        if (string.Equals(drive, _drive, StringComparison.OrdinalIgnoreCase)) return;
        _drive = drive;
        Rebuild(drive);
    }

    private void Rebuild(string drive)
    {
        if (_query != IntPtr.Zero) Native.PdhCloseQuery(_query);
        _query = IntPtr.Zero;
        _counter = IntPtr.Zero;
        _ready = false;

        try
        {
            if (Native.PdhOpenQuery(null, IntPtr.Zero, out _query) != 0) return;

            // LogicalDisk follows the partition; PhysicalDisk is the fallback when
            // that counter set is disabled.
            if (drive.Length > 0 &&
                Native.PdhAddEnglishCounter(_query, $@"\LogicalDisk({drive})\% Disk Time",
                    IntPtr.Zero, out _counter) == 0)
            {
                _ready = true;
            }
            else if (Native.PdhAddEnglishCounter(_query, @"\PhysicalDisk(_Total)\% Disk Time",
                         IntPtr.Zero, out _counter) == 0)
            {
                _ready = true;
            }
            else
            {
                return;
            }

            _ready = Native.PdhCollectQueryData(_query) == 0;
            Native.PdhCollectQueryData(_query); // first sample primes the counter
        }
        catch (Exception ex)
        {
            Log.Error("disk counter", ex);
            _ready = false;
        }
    }

    public double SampleActivity()
    {
        if (!_ready) return double.NaN;
        try
        {
            if (Native.PdhCollectQueryData(_query) != 0) return double.NaN;
            if (Native.PdhGetFormattedCounterValue(_counter, Native.PDH_FMT_DOUBLE, out _, out var value) != 0)
                return double.NaN;
            return double.IsFinite(value.Value) ? Math.Clamp(value.Value, 0, 100) : double.NaN;
        }
        catch
        {
            return double.NaN;
        }
    }

    /// <summary>Used and total bytes of one drive; falls back to every fixed drive.</summary>
    public static (long used, long total) SampleSpace(string drive)
    {
        if (drive.Length > 0)
        {
            try
            {
                var info = new DriveInfo(drive);
                if (info.IsReady) return (info.TotalSize - info.TotalFreeSpace, info.TotalSize);
            }
            catch { /* the drive may be removed */ }
        }

        long used = 0, total = 0;
        try
        {
            foreach (var driveInfo in DriveInfo.GetDrives())
            {
                if (!driveInfo.IsReady || driveInfo.DriveType != DriveType.Fixed) continue;
                used += driveInfo.TotalSize - driveInfo.TotalFreeSpace;
                total += driveInfo.TotalSize;
            }
        }
        catch { /* drives can disappear mid-enumeration */ }
        return (used, total);
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero) Native.PdhCloseQuery(_query);
        _query = IntPtr.Zero;
    }
}

internal static class BatterySampler
{
    public static bool IsPresent() => Sample().has;

    public static (bool has, int percent, bool ac) Sample()
    {
        if (!Native.GetSystemPowerStatus(out var status)) return (false, -1, true);
        var has = (status.BatteryFlag & 128) == 0 && status.BatteryLifePercent != 255;
        return (has, has ? status.BatteryLifePercent : -1, status.ACLineStatus == 1);
    }
}

public sealed class NvidiaGpu : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ShutdownFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CountFn(out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int HandleFn(uint index, out IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int MemoryFn(IntPtr device, out NvmlMemory memory);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int UtilFn(IntPtr device, out NvmlUtilization utilization);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int TempFn(IntPtr device, int sensor, out uint temperature);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FanFn(IntPtr device, int fan, out uint speed);

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMemory { public ulong Total, Free, Used; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization { public uint Gpu, Memory; }

    private IntPtr _library;
    private IntPtr _device;
    private InitFn? _init;
    private ShutdownFn? _shutdown;
    private MemoryFn? _memory;
    private UtilFn? _utilization;
    private TempFn? _temperature;
    private FanFn? _fan;

    public bool Available { get; private set; }

    public void Initialize()
    {
        try
        {
            if (!NativeLibrary.TryLoad("nvml.dll", out _library) &&
                !NativeLibrary.TryLoad(Path.Combine(Environment.SystemDirectory, "nvml.dll"), out _library))
                return;

            _init = Bind<InitFn>("nvmlInit_v2") ?? Bind<InitFn>("nvmlInit");
            if (_init is null) return;
            _shutdown = Bind<ShutdownFn>("nvmlShutdown");
            var count = Bind<CountFn>("nvmlDeviceGetCount_v2");
            var handle = Bind<HandleFn>("nvmlDeviceGetHandleByIndex_v2");
            _memory = Bind<MemoryFn>("nvmlDeviceGetMemoryInfo");
            _utilization = Bind<UtilFn>("nvmlDeviceGetUtilizationRates");
            _temperature = Bind<TempFn>("nvmlDeviceGetTemperature");
            _fan = Bind<FanFn>("nvmlDeviceGetFanSpeed_v2") ?? Bind<FanFn>("nvmlDeviceGetFanSpeed");

            if (count is null || handle is null || _memory is null) return;
            if (_init() != 0) return;
            if (count(out var devices) != 0 || devices == 0) return;
            if (handle(0, out _device) != 0) return;

            Available = true;
        }
        catch (Exception ex)
        {
            Log.Error("nvml init", ex);
            Available = false;
        }
    }

    private T? Bind<T>(string name) where T : Delegate =>
        NativeLibrary.TryGetExport(_library, name, out var address)
            ? Marshal.GetDelegateForFunctionPointer<T>(address)
            : null;

    public void Sample(MetricsSnapshot snapshot)
    {
        if (!Available) return;
        try
        {
            if (_utilization is not null)
            {
                // NVML returns an error for Optimus GPUs that are powered down;
                // reporting 0 % is more useful than a gap in that state.
                snapshot.GpuPercent = _utilization(_device, out var utilization) == 0 ? utilization.Gpu : 0;
            }

            if (_memory is not null && _memory(_device, out var memory) == 0)
            {
                snapshot.VramUsed = (long)memory.Used;
                snapshot.VramTotal = (long)memory.Total;
            }

            if (_temperature is not null && _temperature(_device, 0, out var temperature) == 0 &&
                temperature is > 0 and < 125)
                snapshot.GpuTemp = temperature;

            // Desktop cards stop their fans when idle, so 0 % is a real reading.
            if (_fan is not null && _fan(_device, 0, out var fan) == 0 && fan <= 100)
                snapshot.GpuFan = fan;
        }
        catch (Exception ex)
        {
            Log.Error("nvml sample", ex);
            Available = false;
        }
    }

    public void Dispose()
    {
        try { _shutdown?.Invoke(); } catch { }
        if (_library != IntPtr.Zero) NativeLibrary.Free(_library);
        _library = IntPtr.Zero;
    }
}

public sealed class CpuTemperatures
{
    private readonly object _gate = new();
    private Computer? _computer;
    private IHardware? _cpu;
    private int _started;

    public bool Available { get; private set; }
    public bool InitializationFinished { get; private set; }
    public string Status { get; private set; } = "Reading sensors…";

    public void InitializeAsync()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        Task.Run(Initialize);
    }

    private void Initialize()
    {
        try
        {
            var computer = new Computer { IsCpuEnabled = true };
            computer.Open();
            var cpu = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            if (cpu is null)
            {
                computer.Close();
                Status = "No CPU sensor found.";
                return;
            }

            cpu.Update();
            var hasTemperature = cpu.Sensors.Any(s => s.SensorType == SensorType.Temperature && s.Value.HasValue);

            lock (_gate)
            {
                _computer = computer;
                _cpu = cpu;
                Available = hasTemperature;
            }

            Status = hasTemperature
                ? "CPU temperature available."
                : "No CPU temperature. Run as administrator with the PawnIO driver installed.";
        }
        catch (Exception ex)
        {
            Status = "CPU temperature unavailable: " + ex.Message;
            Log.Error("cpu temperature init", ex);
        }
        finally
        {
            InitializationFinished = true;
            Log.Info("cpu temperature: " + Status);
        }
    }

    public double SampleTemperature()
    {
        lock (_gate)
        {
            if (_computer is null || _cpu is null) return double.NaN;
            try
            {
                _cpu.Update();
                double best = double.NaN;
                foreach (var sensor in _cpu.Sensors)
                {
                    if (sensor.SensorType != SensorType.Temperature || !sensor.Value.HasValue) continue;
                    var name = sensor.Name;
                    if (name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Tctl", StringComparison.OrdinalIgnoreCase))
                        return sensor.Value.Value;
                    best = double.IsNaN(best) ? sensor.Value.Value : Math.Max(best, sensor.Value.Value);
                }
                return best;
            }
            catch
            {
                return double.NaN;
            }
        }
    }

    /// <summary>Fastest core clock in MHz — the number people read as "the" CPU speed.</summary>
    public double SampleClock()
    {
        lock (_gate)
        {
            if (_computer is null || _cpu is null) return double.NaN;
            try
            {
                _cpu.Update();
                double best = double.NaN;
                foreach (var sensor in _cpu.Sensors)
                {
                    if (sensor.SensorType != SensorType.Clock || !sensor.Value.HasValue) continue;
                    var value = sensor.Value.Value;
                    if (value <= 0 || value >= 15000) continue;
                    best = double.IsNaN(best) ? value : Math.Max(best, value);
                }
                return best;
            }
            catch
            {
                return double.NaN;
            }
        }
    }
}
