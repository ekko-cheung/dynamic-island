using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Settings;

namespace DynamicIsland.Providers.SystemStats;

/// <summary>
/// CPU, memory and network speed, sampled once a second. With nothing else on the main island it shows all of them
/// there; while the main island is busy, memory and network move to the small islands instead.
/// </summary>
public sealed class SystemStatsProvider(AppSettings settings, ActivityHub hub) : IActivityProvider
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly SystemStatsActivity _main = new();
    private readonly MemoryStatActivity _memory = new();
    private readonly NetStatActivity _net = new();

    private bool _mainShown, _satellitesShown;
    private ulong _lastIdle, _lastTotal;
    private long _lastSent, _lastReceived;
    private DateTime _lastNetSample;
    private bool _hasCpuBaseline, _hasNetBaseline;

    public event EventHandler<ActivityChangedEventArgs>? ActivityChanged;

    public Task StartAsync()
    {
        _timer.Tick += (_, _) => Tick();
        // Slot changes are handled on the next dispatcher turn: Apply() inside the hub's own notification would re-enter it.
        hub.SlotChanged += (_, slot) =>
        {
            if (slot == IslandSlot.Main) _timer.Dispatcher.BeginInvoke(Publish);
        };
        Tick();
        _timer.Start();
        return Task.CompletedTask;
    }

    private void Tick()
    {
        if (settings.ShowStatsWhenIdle || settings.ShowStatsWhenBusy)
        {
            Sample();
        }
        else
        {
            _hasCpuBaseline = _hasNetBaseline = false; // do not report an average over the time it was off
        }
        Publish();
    }

    private void Sample()
    {
        var cpu = ReadCpu();
        var memory = ReadMemory();
        var (up, down) = ReadNetwork();

        var cpuText = cpu is { } c ? $"{c:0}%" : "–";
        var memoryText = memory is { } m ? $"{m}%" : "–";
        var upText = up is { } u ? FormatSpeed(u) : "–";
        var downText = down is { } d ? FormatSpeed(d) : "–";

        _main.CpuValue = cpu ?? 0;
        _main.MemoryValue = memory ?? 0;
        _memory.MemoryValue = memory ?? 0;
        _main.CpuText = cpuText;
        _main.MemoryText = memoryText;
        _main.UploadText = upText;
        _main.DownloadText = downText;
        _memory.MemoryText = memoryText;
        _net.UploadText = upText;
        _net.DownloadText = downText;
    }

    private void Publish()
    {
        var mainBusy = hub.Get(IslandSlot.Main) is { } current && current is not SystemStatsActivity;

        var wantMain = settings.ShowStatsWhenIdle && !mainBusy;
        var wantSatellites = settings.ShowStatsWhenBusy && mainBusy;

        if (wantMain != _mainShown)
        {
            _mainShown = wantMain;
            ActivityChanged?.Invoke(this, new ActivityChangedEventArgs(_main.Id, wantMain ? _main : null));
        }
        if (wantSatellites != _satellitesShown)
        {
            _satellitesShown = wantSatellites;
            ActivityChanged?.Invoke(this, new ActivityChangedEventArgs(_memory.Id, wantSatellites ? _memory : null));
            ActivityChanged?.Invoke(this, new ActivityChangedEventArgs(_net.Id, wantSatellites ? _net : null));
        }
    }

    private double? ReadCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        var idleTicks = ToUInt64(idle);
        var totalTicks = ToUInt64(kernel) + ToUInt64(user); // kernel time already includes idle time

        double? result = null;
        if (_hasCpuBaseline && totalTicks > _lastTotal)
        {
            var total = totalTicks - _lastTotal;
            var busy = total - (idleTicks - _lastIdle);
            result = Math.Clamp(100.0 * busy / total, 0, 100);
        }
        _lastIdle = idleTicks;
        _lastTotal = totalTicks;
        _hasCpuBaseline = true;
        return result;
    }

    private static int? ReadMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (int)status.MemoryLoad : null;
    }

    private (double? Up, double? Down) ReadNetwork()
    {
        long sent = 0, received = 0;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up
                    || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;
                var stats = nic.GetIPStatistics();
                sent += stats.BytesSent;
                received += stats.BytesReceived;
            }
        }
        catch (NetworkInformationException)
        {
            return (null, null);
        }

        var now = DateTime.UtcNow;
        (double? Up, double? Down) result = (null, null);
        if (_hasNetBaseline)
        {
            var seconds = (now - _lastNetSample).TotalSeconds;
            // An adapter going away makes the sums drop; show 0 for that second instead of a negative speed.
            if (seconds > 0)
                result = (Math.Max(0, sent - _lastSent) / seconds, Math.Max(0, received - _lastReceived) / seconds);
        }
        _lastSent = sent;
        _lastReceived = received;
        _lastNetSample = now;
        _hasNetBaseline = true;
        return result;
    }

    /// <summary>Bytes per second as a short string such as "340K/s" or "1.2M/s".</summary>
    public static string FormatSpeed(double bytesPerSecond)
    {
        const double kb = 1024, mb = kb * 1024, gb = mb * 1024;
        return bytesPerSecond switch
        {
            >= gb => $"{bytesPerSecond / gb:0.0}G/s",
            >= 10 * mb => $"{bytesPerSecond / mb:0}M/s",
            >= mb => $"{bytesPerSecond / mb:0.0}M/s",
            >= kb => $"{bytesPerSecond / kb:0}K/s",
            _ => $"{bytesPerSecond:0}B/s",
        };
    }

    private static ulong ToUInt64(FileTime t) => ((ulong)t.High << 32) | t.Low;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low, High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);
}
