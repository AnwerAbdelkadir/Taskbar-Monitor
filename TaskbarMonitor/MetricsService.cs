namespace TaskbarMonitor;

public sealed class MetricsService : IDisposable
{
    private readonly CpuSampler _cpu = new();
    private readonly NetworkSampler _network = new();
    private readonly DiskSampler _disk = new();
    private readonly NvidiaGpu _gpu = new();
    private Timer? _timer;
    private int _busy;
    private volatile string _diskDrive = "";

    public CpuTemperatures CpuTemps { get; } = new();
    public NvidiaGpu Gpu => _gpu;
    public MetricsSnapshot Latest { get; private set; } = new();
    public bool Paused { get; set; }
    public event Action<MetricsSnapshot>? Updated;

    /// <summary>Drive chosen for the Disk widget; empty means the Windows drive.</summary>
    public void SetDiskDrive(string? drive) => _diskDrive = AppSettings.NormalizeDrive(drive);

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

            var drive = DiskSampler.Resolve(_diskDrive);
            _disk.SetDrive(drive);
            snapshot.DiskDrive = drive;
            snapshot.DiskActivity = _disk.SampleActivity();
            (snapshot.DiskUsed, snapshot.DiskTotal) = DiskSampler.SampleSpace(drive);

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
        _disk.Dispose();
    }
}
