namespace TaskbarMonitor;

public sealed class MetricsService : IDisposable
{
    private readonly CpuSampler _cpu = new();
    private readonly NetworkSampler _network = new();
    private readonly Dictionary<string, DiskSampler> _disks = new(StringComparer.OrdinalIgnoreCase);
    private readonly NvidiaGpu _gpu = new();
    private Timer? _timer;
    private int _busy;
    private readonly List<string> _diskDrives = new();

    public CpuTemperatures CpuTemps { get; } = new();
    public NvidiaGpu Gpu => _gpu;
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
        _gpu.Initialize();
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

            _gpu.Sample(snapshot);

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
        foreach (var sampler in _disks.Values) sampler.Dispose();
        _disks.Clear();
    }
}
