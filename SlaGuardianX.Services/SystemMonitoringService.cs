using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace SlaGuardianX.Services;

/// <summary>
/// Real Windows system telemetry: CPU, RAM, Disk, Network, Processes.
/// Collects live data every 1-2 seconds using WMI and .NET APIs.
/// </summary>
public sealed class SystemMonitoringService : IDisposable
{
    private System.Threading.Timer? _pollingTimer;
    private readonly object _lock = new();
    private bool _isMonitoring;
    private readonly List<SystemSnapshot> _history = new();
    private const int MaxHistory = 600; // ~10 min at 1s

    // Cached counters
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _diskCounter;

    public event EventHandler<SystemSnapshot>? SnapshotCaptured;

    public bool IsMonitoring => _isMonitoring;
    public IReadOnlyList<SystemSnapshot> History => _history.AsReadOnly();

    public SystemMonitoringService()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
            _cpuCounter.NextValue(); // prime
        }
        catch { _cpuCounter = null; }

        try
        {
            _diskCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", true);
            _diskCounter.NextValue();
        }
        catch { _diskCounter = null; }
    }

    public void Start(int intervalMs = 2000)
    {
        if (_isMonitoring) return;
        _isMonitoring = true;
        _pollingTimer = new System.Threading.Timer(async _ => await CaptureSnapshot(), null, 0, intervalMs);
    }

    public void Stop()
    {
        _isMonitoring = false;
        _pollingTimer?.Dispose();
        _pollingTimer = null;
    }

    private async Task CaptureSnapshot()
    {
        try
        {
            var snap = await Task.Run(() => CollectMetrics());
            lock (_lock)
            {
                _history.Add(snap);
                if (_history.Count > MaxHistory)
                    _history.RemoveAt(0);
            }
            SnapshotCaptured?.Invoke(this, snap);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemMonitoringService] Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Single-shot collection of all system metrics.
    /// </summary>
    public SystemSnapshot CollectMetrics()
    {
        var snap = new SystemSnapshot { Timestamp = DateTime.Now };

        // --- CPU ---
        try
        {
            snap.CpuPercent = _cpuCounter?.NextValue() ?? GetCpuViaWmi();
        }
        catch { snap.CpuPercent = GetCpuViaWmi(); }

        // --- RAM ---
        try
        {
            var ci = new Microsoft.VisualBasic.Devices.ComputerInfo();
            snap.TotalRamMB = (long)(ci.TotalPhysicalMemory / 1024 / 1024);
            snap.AvailableRamMB = (long)(ci.AvailablePhysicalMemory / 1024 / 1024);
            snap.UsedRamMB = snap.TotalRamMB - snap.AvailableRamMB;
            snap.RamPercent = snap.TotalRamMB > 0 ? Math.Round((double)snap.UsedRamMB / snap.TotalRamMB * 100, 1) : 0;
        }
        catch { }

        // --- Disk ---
        try
        {
            snap.DiskActivePercent = _diskCounter?.NextValue() ?? 0;
            var drives = System.IO.DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == System.IO.DriveType.Fixed).ToList();
            snap.TotalDiskGB = drives.Sum(d => d.TotalSize / 1_073_741_824.0);
            snap.FreeDiskGB = drives.Sum(d => d.AvailableFreeSpace / 1_073_741_824.0);
            snap.UsedDiskGB = snap.TotalDiskGB - snap.FreeDiskGB;
            snap.DiskUsagePercent = snap.TotalDiskGB > 0 ? Math.Round(snap.UsedDiskGB / snap.TotalDiskGB * 100, 1) : 0;
        }
        catch { }

        // --- Network ---
        try
        {
            var ifaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();

            long sentBefore = ifaces.Sum(n => n.GetIPv4Statistics().BytesSent);
            long recvBefore = ifaces.Sum(n => n.GetIPv4Statistics().BytesReceived);
            Thread.Sleep(500);
            long sentAfter = ifaces.Sum(n => n.GetIPv4Statistics().BytesSent);
            long recvAfter = ifaces.Sum(n => n.GetIPv4Statistics().BytesReceived);

            snap.NetworkUploadBps = (sentAfter - sentBefore) * 2; // scale for half-second
            snap.NetworkDownloadBps = (recvAfter - recvBefore) * 2;
            snap.NetworkUploadMbps = Math.Round(snap.NetworkUploadBps / 125000.0, 2);
            snap.NetworkDownloadMbps = Math.Round(snap.NetworkDownloadBps / 125000.0, 2);
            snap.ActiveAdapters = ifaces.Count;
        }
        catch { }

        // --- Processes ---
        try
        {
            snap.ProcessCount = Process.GetProcesses().Length;
        }
        catch { }

        // --- Uptime ---
        try
        {
            snap.SystemUptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        }
        catch { snap.SystemUptime = TimeSpan.Zero; }

        return snap;
    }

    private static float GetCpuViaWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT LoadPercentage FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                var val = obj["LoadPercentage"];
                if (val != null) return Convert.ToSingle(val);
            }
        }
        catch { }
        return 0f;
    }

    public List<SystemSnapshot> GetRecentSnapshots(int count)
    {
        lock (_lock) { return _history.TakeLast(Math.Min(count, _history.Count)).ToList(); }
    }

    public void Dispose()
    {
        Stop();
        _cpuCounter?.Dispose();
        _diskCounter?.Dispose();
    }
}

// --------------- Data models ---------------

public class SystemSnapshot
{
    public DateTime Timestamp { get; set; }

    // CPU
    public double CpuPercent { get; set; }

    // RAM
    public long TotalRamMB { get; set; }
    public long AvailableRamMB { get; set; }
    public long UsedRamMB { get; set; }
    public double RamPercent { get; set; }

    // Disk
    public double DiskActivePercent { get; set; }
    public double TotalDiskGB { get; set; }
    public double FreeDiskGB { get; set; }
    public double UsedDiskGB { get; set; }
    public double DiskUsagePercent { get; set; }

    // Network
    public long NetworkUploadBps { get; set; }
    public long NetworkDownloadBps { get; set; }
    public double NetworkUploadMbps { get; set; }
    public double NetworkDownloadMbps { get; set; }
    public int ActiveAdapters { get; set; }

    // System
    public int ProcessCount { get; set; }
    public TimeSpan SystemUptime { get; set; }
}
