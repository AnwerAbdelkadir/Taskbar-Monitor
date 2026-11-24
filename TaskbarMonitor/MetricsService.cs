namespace TaskbarMonitor;

public sealed class MetricsService : IDisposable
{
    private readonly CpuSampler _cpu = new();
    private readonly NetworkSampler _network = new();
    private readonly Dictionary<string, DiskSampler> _disks = new(StringComparer.OrdinalIgnoreCase);
    private readonly NvidiaGpu _gpu = new();
    private readonly LhmGpu _gpuFallback = new();
    private bool _gpuFallbackActive;
    private Timer? _timer;
    private int _busy;
    private readonly List<string> _diskDrives = new();

    public CpuTemperatures CpuTemps { get; } = new();
    public bool GpuAvailable => _gpuFallbackActive ? _gpuFallback.Available : _gpu.Available;
    public string GpuStatus => _gpuFallbackActive
        ? _gpuFallback.Available
            ? "GPU sensors — LibreHardwareMonitor ready."
            : "GPU sensors — no supported GPU detected; GPU values show as --."
        : _gpu.Available
            ? "GPU sensors — NVML ready."
            : "GPU sensors — no NVIDIA GPU detected; GPU values show as --.";
    public MetricsSnapshot Latest { get; private set; } = new();
    public bool Paused { get; set; }
    public event Action<MetricsSnapshot>? Updated;

    /// <summary>Drives the Disk widgets follow; a null entry means the Windows drive.</summary>
    public void SetDiskDrives(IEnumerable<string?>? drives)
    {
        _diskDrives.Clear();
        foreach (var drive in drives ?? Enumerable.Empty<string?>())
        {
            var resolved = DiskSampler.Resolve(drive);
            if (resolved.Length > 0 && !_diskDrives.Contains(resolved))
                _diskDrives.Add(resolved);
        }

        foreach (var stale in _disks.Where(kv => !_diskDrives.Contains(kv.Key)).ToList())
        {
            stale.Value.Dispose();
            _disks.Remove(stale.Key);
        }
    }

    public void SetAdapter(string? id) => _network.SetAdapter(id);

    public void Start(int intervalMs)
    {
        // NVML when it works (NVIDIA); otherwise LibreHardwareMonitor, which also
        // covers Intel and AMD. TASKBARMONITOR_GPU_SOURCE=lhm forces the fallback so
        // it can be exercised on an NVIDIA machine.
        var forceFallback = string.Equals(
            Environment.GetEnvironmentVariable("TASKBARMONITOR_GPU_SOURCE"),
            "lhm",
            StringComparison.OrdinalIgnoreCase);

        if (!forceFallback) _gpu.Initialize();
        _gpuFallbackActive = forceFallback || !_gpu.Available;
        if (_gpuFallbackActive) _gpuFallback.Initialize();

        CpuTemps.InitializeAsync();
        _timer = new Timer(Tick, null, 0, Math.Clamp(intervalMs, 250, 10000));
    }

    public void SetInterval(int intervalMs)
    {
        try { _timer?.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(Math.Clamp(intervalMs, 250, 10000))); }
        catch (ObjectDisposedException) { }
    }

    private void Tick(object? _)
    {
        if (Paused || Interlocked.Exchange(ref _busy, 1) != 0) return;
        try
        {
            var snapshot = new MetricsSnapshot
            {
                CpuPercent = _cpu.Sample(),
                CpuTemp = CpuTemps.SampleTemperature(),
                CpuClock = CpuTemps.SampleClock()
            };

            (snapshot.RamUsed, snapshot.RamTotal) = MemorySampler.Sample();
            (snapshot.NetUp, snapshot.NetDown) = _network.Sample();

            if (_gpuFallbackActive) _gpuFallback.Sample(snapshot);
            else _gpu.Sample(snapshot);

            foreach (var drive in _diskDrives)
            {
                if (!_disks.TryGetValue(drive, out var sampler))
                {
                    sampler = new DiskSampler();
                    _disks[drive] = sampler;
                }

                sampler.SetDrive(drive);
                var reading = new DiskReading { Activity = sampler.SampleActivity() };
                (reading.Used, reading.Total) = DiskSampler.SampleSpace(drive);
                snapshot.Disks[drive] = reading;
            }

            (snapshot.HasBattery, snapshot.BatteryPercent, snapshot.OnAc) = BatterySampler.Sample();

            History.Add(snapshot);

            Latest = snapshot;
            Updated?.Invoke(snapshot);
        }
        catch (Exception ex)
        {
            Log.Error("metrics tick", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        _gpu.Dispose();
        _gpuFallback.Dispose();
        foreach (var sampler in _disks.Values) sampler.Dispose();
        _disks.Clear();
    }
}

public readonly record struct HistorySample(
    double Cpu, double Gpu, double Ram, double NetUp, double NetDown, double CpuTemp, double GpuTemp);

/// <summary>Rolling window the graphs window draws from; two minutes at 1 s.</summary>
public static class History
{
    public const int Capacity = 120;

    private static readonly object Gate = new();
    private static readonly HistorySample[] Buffer = new HistorySample[Capacity];
    private static int _count;
    private static int _head;

    public static void Add(MetricsSnapshot s)
    {
        lock (Gate)
        {
            var ram = s.RamTotal > 0 ? s.RamUsed * 100.0 / s.RamTotal : double.NaN;
            Buffer[_head] = new HistorySample(
                s.CpuPercent, s.GpuPercent, ram, s.NetUp, s.NetDown, s.CpuTemp, s.GpuTemp);
            _head = (_head + 1) % Capacity;
            _count = Math.Min(_count + 1, Capacity);
        }
    }

    /// <summary>Oldest first, up to <see cref="Capacity"/> samples.</summary>
    public static HistorySample[] Samples()
    {
        lock (Gate)
        {
            var result = new HistorySample[_count];
            for (var i = 0; i < _count; i++)
                result[i] = Buffer[(_head - _count + i + Capacity * 2) % Capacity];
            return result;
        }
    }
}
