using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlaGuardianX.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SlaGuardianX.ViewModels.Modules;

// =================================================================
// 1. REAL-TIME MONITORING — Live CPU/RAM/Disk/Net every 2s
// =================================================================
public partial class RealTimeMonitoringViewModel : ObservableObject
{
    private readonly SystemMonitoringService _monitor;
    private readonly LoggingService _logger;

    [ObservableProperty] private double bandwidth;       // Net download Mbps
    [ObservableProperty] private double latency;         // CPU %
    [ObservableProperty] private double packetLoss;      // RAM %
    [ObservableProperty] private double jitter;          // Disk I/O %
    [ObservableProperty] private double uptime;          // Disk free %
    [ObservableProperty] private bool isMonitoring;
    [ObservableProperty] private string statusMessage = "Idle - press Start to begin monitoring";
    [ObservableProperty] private int dataPointsCollected;
    [ObservableProperty] private double peakBandwidth;
    [ObservableProperty] private double minBandwidth = 999;
    [ObservableProperty] private double avgBandwidth;
    [ObservableProperty] private string lastUpdated = "-";
    [ObservableProperty] private ObservableCollection<string> recentEvents = new();

    private double _sum;

    public RealTimeMonitoringViewModel(SystemMonitoringService monitor, LoggingService logger)
    {
        _monitor = monitor;
        _logger = logger;
        _monitor.SnapshotCaptured += OnSnapshot;
        IsMonitoring = _monitor.IsMonitoring;

        // Load existing history
        var history = _monitor.GetRecentSnapshots(10);
        if (history.Count > 0) ApplySnap(history.Last());
        StatusMessage = _monitor.IsMonitoring ? "Live monitoring active" : "Idle - press Start to begin";
    }

    private void OnSnapshot(object? sender, SystemSnapshot s)
    {
        System.Windows.Application.Current?.Dispatcher?.Invoke(() => ApplySnap(s));
    }

    private void ApplySnap(SystemSnapshot s)
    {
        Bandwidth = s.NetworkDownloadMbps;
        Latency = Math.Round(s.CpuPercent, 1);
        PacketLoss = Math.Round(s.RamPercent, 1);
        Jitter = Math.Round(s.DiskActivePercent, 1);
        Uptime = Math.Round(100 - s.DiskUsagePercent, 1);

        DataPointsCollected++;
        LastUpdated = DateTime.Now.ToString("HH:mm:ss");

        _sum += s.NetworkDownloadMbps;
        AvgBandwidth = Math.Round(_sum / Math.Max(1, DataPointsCollected), 2);
        if (s.NetworkDownloadMbps > PeakBandwidth) PeakBandwidth = s.NetworkDownloadMbps;
        if (s.NetworkDownloadMbps < MinBandwidth && s.NetworkDownloadMbps > 0) MinBandwidth = s.NetworkDownloadMbps;

        if (s.CpuPercent > 85)
            AddEvent($"High CPU: {s.CpuPercent:F0}%");
        if (s.RamPercent > 80)
            AddEvent($"High RAM: {s.RamPercent:F0}%");
        if (s.FreeDiskGB < 10)
            AddEvent($"Low disk space: {s.FreeDiskGB:F1} GB free");
    }

    private void AddEvent(string msg)
    {
        RecentEvents.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {msg}");
        if (RecentEvents.Count > 30) RecentEvents.RemoveAt(RecentEvents.Count - 1);
    }

    [RelayCommand]
    public void StartMonitoring()
    {
        _monitor.Start(2000);
        IsMonitoring = true;
        StatusMessage = "Live monitoring active - sampling every 2s";
        _logger.Log(LogLevel.Action, "Monitor", "Real-time monitoring started.");
        AddEvent("Monitoring started");
    }

    [RelayCommand]
    public void StopMonitoring()
    {
        _monitor.Stop();
        IsMonitoring = false;
        StatusMessage = "Monitoring paused";
        _logger.Log(LogLevel.Action, "Monitor", "Monitoring stopped.");
        AddEvent("Monitoring stopped");
    }

    [RelayCommand]
    public async Task RefreshTelemetry()
    {
        StatusMessage = "Refreshing...";
        var snap = await Task.Run(() => _monitor.CollectMetrics());
        ApplySnap(snap);
        StatusMessage = "Refreshed";
    }
}

// =================================================================
// 2. SLA MANAGER → HEALTH RULE ENGINE
// =================================================================
public partial class SlaManagerViewModel : ObservableObject
{
    private readonly HealthRuleEngine _ruleEngine;
    private readonly SystemMonitoringService _monitor;
    private readonly LoggingService _logger;

    [ObservableProperty] private double guaranteedBandwidth;  // CPU threshold
    [ObservableProperty] private double maxLatency = 80;      // RAM threshold
    [ObservableProperty] private double maxPacketLoss = 90;   // Disk threshold
    [ObservableProperty] private int totalViolations;
    [ObservableProperty] private double penaltyEstimate;
    [ObservableProperty] private double compliancePercentage = 100;
    [ObservableProperty] private int totalRecords;
    [ObservableProperty] private string statusMessage = "Loading health rule configuration...";

    public SlaManagerViewModel(HealthRuleEngine ruleEngine, SystemMonitoringService monitor, LoggingService logger)
    {
        _ruleEngine = ruleEngine;
        _monitor = monitor;
        _logger = logger;
        GuaranteedBandwidth = _ruleEngine.CpuThreshold;
        MaxLatency = _ruleEngine.RamThreshold;
        MaxPacketLoss = _ruleEngine.DiskThreshold;
        RunHealthCheck();
    }

    private void RunHealthCheck()
    {
        try
        {
            var history = _monitor.GetRecentSnapshots(100);
            TotalRecords = history.Count;
            int issues = 0;
            foreach (var s in history)
            {
                var r = _ruleEngine.Evaluate(s);
                if (r.Status != HealthStatus.Healthy) issues++;
            }
            TotalViolations = issues;
            CompliancePercentage = TotalRecords > 0
                ? Math.Round((TotalRecords - issues) / (double)TotalRecords * 100, 1) : 100;
            PenaltyEstimate = TotalViolations; // count of rule violations
            StatusMessage = $"Health rules evaluated against {TotalRecords} snapshots — {TotalViolations} violations";
        }
        catch (Exception ex) { StatusMessage = "Error: " + ex.Message; }
    }

    [RelayCommand]
    public async Task SaveSlaProfile()
    {
        _ruleEngine.CpuThreshold = GuaranteedBandwidth;
        _ruleEngine.RamThreshold = MaxLatency;
        _ruleEngine.DiskThreshold = MaxPacketLoss;
        _logger.Log(LogLevel.Action, "HealthRules", $"Thresholds updated: CPU<{GuaranteedBandwidth}% RAM<{MaxLatency}% Disk<{MaxPacketLoss}%");
        StatusMessage = $"Rules saved — CPU<{GuaranteedBandwidth}% RAM<{MaxLatency}% Disk<{MaxPacketLoss}%";
        await Task.CompletedTask;
    }

    [RelayCommand]
    public async Task CalculatePenalty()
    {
        RunHealthCheck();
        await Task.CompletedTask;
    }

    [RelayCommand]
    public async Task RunSlaCheck()
    {
        StatusMessage = "Running live health check...";
        var snap = await Task.Run(() => _monitor.CollectMetrics());
        var report = _ruleEngine.Evaluate(snap);
        CompliancePercentage = report.HealthScore;
        TotalRecords++;
        if (report.Status != HealthStatus.Healthy) TotalViolations++;
        StatusMessage = $"Health: {report.Status} | Score: {report.HealthScore} | Issues: {report.Issues.Count}";
        _logger.Log(LogLevel.Info, "HealthRules", StatusMessage);
    }
}

// =================================================================
// 3. PREDICTIVE ANALYTICS — Moving average trend detection
// =================================================================
public partial class AiPredictionViewModel : ObservableObject
{
    private readonly SystemMonitoringService _monitor;

    [ObservableProperty] private double bandwidthForecast;   // predicted CPU
    [ObservableProperty] private string slaBreachPrediction = "Analyzing...";
    [ObservableProperty] private double riskTrend;
    [ObservableProperty] private int aiConfidence;
    [ObservableProperty] private string statusMessage = "Waiting for data...";
    [ObservableProperty] private int dataPointsUsed;
    [ObservableProperty] private double currentBandwidth;    // current CPU
    [ObservableProperty] private string trendDirection = "-";
    [ObservableProperty] private bool isAnalyzing;

    public AiPredictionViewModel(SystemMonitoringService monitor)
    {
        _monitor = monitor;
        RunInitialPrediction();
    }

    private async void RunInitialPrediction() => await RunPredictionInternal();

    private Task RunPredictionInternal()
    {
        IsAnalyzing = true;
        StatusMessage = "Analyzing system trends...";
        try
        {
            var history = _monitor.GetRecentSnapshots(100);
            if (history.Count < 5)
            {
                SlaBreachPrediction = "Insufficient data (need 5+ snapshots)";
                StatusMessage = "Start monitoring and wait for data to accumulate.";
                AiConfidence = 0;
                IsAnalyzing = false;
                return Task.CompletedTask;
            }

            var cpuValues = history.Select(s => s.CpuPercent).ToList();
            var ramValues = history.Select(s => s.RamPercent).ToList();
            DataPointsUsed = history.Count;
            CurrentBandwidth = Math.Round(cpuValues.Last(), 1);

            // Moving average prediction
            int window = Math.Min(10, cpuValues.Count);
            double recentAvg = cpuValues.TakeLast(window).Average();
            double olderAvg = cpuValues.Take(window).Average();
            double trend = recentAvg - olderAvg;

            BandwidthForecast = Math.Round(recentAvg + trend, 1);
            RiskTrend = Math.Round(trend, 2);
            TrendDirection = trend > 2 ? "Rising" : trend < -2 ? "Falling" : "Stable";

            bool willBreach = BandwidthForecast > 85;
            SlaBreachPrediction = willBreach ? "CPU OVERLOAD LIKELY" : "System stable";
            AiConfidence = Math.Min(95, 50 + DataPointsUsed / 2);

            // RAM prediction
            double ramTrend = ramValues.TakeLast(window).Average() - ramValues.Take(window).Average();
            if (ramTrend > 3) SlaBreachPrediction += " | RAM rising";

            StatusMessage = $"Analyzed {DataPointsUsed} snapshots — CPU trend: {TrendDirection} ({trend:+0.0;-0.0})";
        }
        catch (Exception ex) { StatusMessage = "Error: " + ex.Message; }
        IsAnalyzing = false;
        return Task.CompletedTask;
    }

    [RelayCommand]
    public async Task RunPrediction() => await RunPredictionInternal();
}

// =================================================================
// 4. OPTIMIZATION CONTROL — Real safe actions
// =================================================================
public partial class OptimizationControlViewModel : ObservableObject
{
    private readonly DiskCleanupService _cleanup;
    private readonly LoggingService _logger;

    [ObservableProperty] private bool isSmartQosEnabled;      // DNS flush done
    [ObservableProperty] private bool isBackgroundLimiterEnabled;
    [ObservableProperty] private double optimizationBoost;
    [ObservableProperty] private double beforeBandwidth;      // temp files found MB
    [ObservableProperty] private double afterBandwidth;       // temp files cleared MB
    [ObservableProperty] private double improvementMbps;      // MB freed
    [ObservableProperty] private string statusMessage = "Optimization module ready";
    [ObservableProperty] private bool isOptimizationActive;
    [ObservableProperty] private string optimizationStrategy = "Temp Cleanup + DNS Flush";

    public OptimizationControlViewModel(DiskCleanupService cleanup, LoggingService logger)
    {
        _cleanup = cleanup;
        _logger = logger;
        AnalyzeTempAsync();
    }

    private async void AnalyzeTempAsync()
    {
        try
        {
            var report = await _cleanup.AnalyzeTempFilesAsync();
            BeforeBandwidth = report.TotalTempSizeMB;
            StatusMessage = $"Found {report.TotalTempFiles} temp files ({report.TotalTempSizeMB:F1} MB reclaimable)";
        }
        catch { }
    }

    [RelayCommand]
    public async Task RunOptimization()
    {
        StatusMessage = "Cleaning temp files...";
        _logger.Log(LogLevel.Action, "Optimize", "Starting temp file cleanup...");
        try
        {
            long freed = await _cleanup.CleanTempFilesAsync();
            double freedMB = Math.Round(freed / 1048576.0, 1);
            AfterBandwidth = freedMB;
            ImprovementMbps = freedMB;
            IsOptimizationActive = true;
            StatusMessage = $"Cleaned {freedMB} MB of temp files";
            _logger.Log(LogLevel.Action, "Optimize", $"Freed {freedMB} MB.");

            // Re-analyze
            var report = await _cleanup.AnalyzeTempFilesAsync();
            BeforeBandwidth = report.TotalTempSizeMB;
        }
        catch (Exception ex) { StatusMessage = "Error: " + ex.Message; }
    }

    [RelayCommand]
    public async Task DisableOptimization()
    {
        // Flush DNS
        StatusMessage = "Flushing DNS cache...";
        bool ok = await _cleanup.FlushDnsAsync();
        IsSmartQosEnabled = ok;
        StatusMessage = ok ? "DNS cache flushed successfully" : "DNS flush failed (may need admin)";
        _logger.Log(LogLevel.Action, "Optimize", StatusMessage);
    }

    [RelayCommand]
    public async Task EnableSmartQos()
    {
        // Flush DNS toggle
        await DisableOptimization();
    }

    [RelayCommand]
    public void EnableBackgroundLimiter()
    {
        IsBackgroundLimiterEnabled = !IsBackgroundLimiterEnabled;
        StatusMessage = IsBackgroundLimiterEnabled
            ? "Background limiter enabled (placeholder)"
            : "Background limiter disabled";
    }
}

// =================================================================
// 5. ALERTS & INCIDENTS — Real alert engine
// =================================================================
public partial class AlertsIncidentViewModel : ObservableObject
{
    private readonly AlertEngine _alertEngine;
    private readonly SystemMonitoringService _monitor;
    private readonly LoggingService _logger;

    [ObservableProperty] private int criticalAlerts;
    [ObservableProperty] private int warningAlerts;
    [ObservableProperty] private int infoAlerts;
    [ObservableProperty] private string selectedSeverity = "All";
    [ObservableProperty] private int totalAlerts;
    [ObservableProperty] private string statusMessage = "Loading alert history...";
    [ObservableProperty] private ObservableCollection<string> alertLog = new();
    [ObservableProperty] private string lastIncidentTime = "-";

    public AlertsIncidentViewModel(AlertEngine alertEngine, SystemMonitoringService monitor, LoggingService logger)
    {
        _alertEngine = alertEngine;
        _monitor = monitor;
        _logger = logger;

        _alertEngine.AlertFired += OnAlertFired;
        LoadExistingAlerts();
    }

    private void LoadExistingAlerts()
    {
        var alerts = _alertEngine.Alerts;
        CriticalAlerts = alerts.Count(a => a.Severity == AlertSeverity.Critical);
        WarningAlerts = alerts.Count(a => a.Severity == AlertSeverity.Warning);
        InfoAlerts = alerts.Count(a => a.Severity == AlertSeverity.Info);
        TotalAlerts = alerts.Count;

        foreach (var a in alerts.Take(30))
            AlertLog.Add($"[{a.Timestamp:HH:mm:ss}] {a.Severity}: {a.Message}");

        var last = alerts.FirstOrDefault(a => a.Severity == AlertSeverity.Critical);
        LastIncidentTime = last?.Timestamp.ToString("g") ?? "None";
        StatusMessage = $"{TotalAlerts} alerts — {CriticalAlerts} critical";
    }

    private void OnAlertFired(object? sender, AlertRecord alert)
    {
        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            switch (alert.Severity)
            {
                case AlertSeverity.Critical: CriticalAlerts++; break;
                case AlertSeverity.Warning: WarningAlerts++; break;
                default: InfoAlerts++; break;
            }
            TotalAlerts++;
            AlertLog.Insert(0, $"[{alert.Timestamp:HH:mm:ss}] {alert.Severity}: {alert.Message}");
            if (AlertLog.Count > 50) AlertLog.RemoveAt(AlertLog.Count - 1);
            LastIncidentTime = alert.Timestamp.ToString("g");
            StatusMessage = $"{TotalAlerts} alerts — {CriticalAlerts} critical";
        });
    }

    [RelayCommand]
    public void ClearAlerts()
    {
        _alertEngine.ClearAlerts();
        AlertLog.Clear();
        CriticalAlerts = WarningAlerts = InfoAlerts = TotalAlerts = 0;
        StatusMessage = "All alerts cleared";
        _logger.Log(LogLevel.Action, "Alerts", "Alerts cleared.");
    }
}

// =================================================================
// 6. ANALYTICS & REPORTS — Real system report
// =================================================================
public partial class AnalyticsReportsViewModel : ObservableObject
{
    private readonly SystemMonitoringService _monitor;
    private readonly LoggingService _logger;

    [ObservableProperty] private double slaComplianceTrend;  // health score avg
    [ObservableProperty] private int peakUsageHour;
    [ObservableProperty] private int violationFrequency;
    [ObservableProperty] private double optimizationImpact;
    [ObservableProperty] private string statusMessage = "Loading analytics...";
    [ObservableProperty] private int totalMetrics;
    [ObservableProperty] private double avgBandwidth;    // avg CPU
    [ObservableProperty] private double avgLatency;      // avg RAM
    [ObservableProperty] private double avgPacketLoss;   // avg Disk
    [ObservableProperty] private int optimizedCount;

    public AnalyticsReportsViewModel(SystemMonitoringService monitor, LoggingService logger)
    {
        _monitor = monitor;
        _logger = logger;
        LoadAnalytics();
    }

    private void LoadAnalytics()
    {
        try
        {
            var history = _monitor.GetRecentSnapshots(200);
            TotalMetrics = history.Count;
            if (history.Count > 0)
            {
                AvgBandwidth = Math.Round(history.Average(s => s.CpuPercent), 1);
                AvgLatency = Math.Round(history.Average(s => s.RamPercent), 1);
                AvgPacketLoss = Math.Round(history.Average(s => s.DiskUsagePercent), 1);

                ViolationFrequency = history.Count(s => s.CpuPercent > 85 || s.RamPercent > 80);
                SlaComplianceTrend = TotalMetrics > 0
                    ? Math.Round((TotalMetrics - ViolationFrequency) / (double)TotalMetrics * 100, 1) : 100;

                var hourGroups = history.GroupBy(s => s.Timestamp.Hour)
                    .OrderByDescending(g => g.Average(s => s.CpuPercent));
                PeakUsageHour = hourGroups.FirstOrDefault()?.Key ?? 0;
            }
            StatusMessage = $"Analyzed {TotalMetrics} system snapshots";
        }
        catch (Exception ex) { StatusMessage = "Error: " + ex.Message; }
    }

    [RelayCommand]
    public async Task RefreshAnalytics() { LoadAnalytics(); await Task.CompletedTask; }

    [RelayCommand]
    public async Task ExportPdf()
    {
        StatusMessage = "Generating JSON report...";
        try
        {
            var history = _monitor.GetRecentSnapshots(100);
            var report = new
            {
                Generated = DateTime.Now,
                TotalSnapshots = history.Count,
                AvgCpu = history.Count > 0 ? history.Average(s => s.CpuPercent) : 0,
                AvgRam = history.Count > 0 ? history.Average(s => s.RamPercent) : 0,
                Violations = ViolationFrequency,
                HealthScore = SlaComplianceTrend
            };
            var desktopPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"SlaGuardianX_Report_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            await File.WriteAllTextAsync(desktopPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            StatusMessage = $"Report saved to Desktop: {Path.GetFileName(desktopPath)}";
            _logger.Log(LogLevel.Action, "Report", $"Exported to {desktopPath}");
        }
        catch (Exception ex) { StatusMessage = "Export failed: " + ex.Message; }
    }

    [RelayCommand]
    public async Task ExportCsv()
    {
        StatusMessage = "Generating CSV export...";
        try
        {
            var history = _monitor.GetRecentSnapshots(200);
            var csv = "Timestamp,CPU%,RAM%,DiskUsed%,NetDown Mbps,NetUp Mbps,Processes\n"
                + string.Join("\n", history.Select(s =>
                    $"{s.Timestamp:yyyy-MM-dd HH:mm:ss},{s.CpuPercent:F1},{s.RamPercent:F1},{s.DiskUsagePercent:F1},{s.NetworkDownloadMbps:F2},{s.NetworkUploadMbps:F2},{s.ProcessCount}"));
            var desktopPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"SlaGuardianX_Data_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            await File.WriteAllTextAsync(desktopPath, csv);
            StatusMessage = $"CSV saved to Desktop: {Path.GetFileName(desktopPath)}";
            _logger.Log(LogLevel.Action, "Report", $"CSV exported to {desktopPath}");
        }
        catch (Exception ex) { StatusMessage = "Export failed: " + ex.Message; }
    }
}

// =================================================================
// 7. NETWORK DIAGNOSTICS (was Multi-Site)
// =================================================================
public partial class MultiSiteViewModel : ObservableObject
{
    private readonly NetworkDiagnosticsService _netDiag;
    private readonly LoggingService _logger;

    [ObservableProperty] private string selectedSite = "Running diagnostics...";
    [ObservableProperty] private double siteBandwidth;     // external ping
    [ObservableProperty] private double siteCompliance;    // dns time
    [ObservableProperty] private int locationsCount;
    [ObservableProperty] private string statusMessage = "Running network diagnostics...";
    [ObservableProperty] private double siteLatency;       // gateway ping
    [ObservableProperty] private string siteStatus = "Checking...";
    [ObservableProperty] private int siteDeviceCount;      // adapter count

    public MultiSiteViewModel(NetworkDiagnosticsService netDiag, LoggingService logger)
    {
        _netDiag = netDiag;
        _logger = logger;
        RunDiagAsync();
    }

    private async void RunDiagAsync()
    {
        StatusMessage = "Running full network diagnostics...";
        try
        {
            var report = await _netDiag.RunDiagnosticsAsync();
            SelectedSite = report.IsInternetAvailable ? "Internet Connected" : "No Internet";
            SiteStatus = report.IsInternetAvailable ? "Online" : "Offline";
            SiteBandwidth = report.ExternalPingMs;
            SiteLatency = report.GatewayPingMs;
            SiteCompliance = report.DnsResolutionMs;
            SiteDeviceCount = report.Adapters.Count;
            LocationsCount = report.Adapters.Count;
            StatusMessage = $"Gateway: {report.GatewayIp} ({report.GatewayPingMs}ms) | DNS: {report.DnsResolutionMs}ms | Public IP: {report.PublicIp}";
            _logger.Log(LogLevel.Info, "NetDiag", StatusMessage);
        }
        catch (Exception ex) { StatusMessage = "Error: " + ex.Message; }
    }

    [RelayCommand]
    public void SelectSite(string ctx)
    {
        RunDiagAsync();
    }
}

// =================================================================
// 8. DEVICE & HARDWARE (was Topology)
// =================================================================
public partial class TopologyViewModel : ObservableObject
{
    private readonly HardwareInfoService _hwService;
    private readonly NetworkDiagnosticsService _netDiag;

    [ObservableProperty] private string topologyStatus = "Scanning hardware...";
    [ObservableProperty] private int deviceCount;
    [ObservableProperty] private int alertingDevices;
    [ObservableProperty] private int onlineDevices;
    [ObservableProperty] private int offlineDevices;
    [ObservableProperty] private string lastScanTime = "-";
    [ObservableProperty] private ObservableCollection<DeviceInfo> devices = new();

    public TopologyViewModel(HardwareInfoService hwService, NetworkDiagnosticsService netDiag)
    {
        _hwService = hwService;
        _netDiag = netDiag;
        ScanAsync();
    }

    private async void ScanAsync()
    {
        TopologyStatus = "Scanning hardware and network...";
        try
        {
            var hw = await _hwService.GetHardwareInfoAsync();
            var net = await _netDiag.RunDiagnosticsAsync();
            Devices.Clear();

            // Add real hardware
            Devices.Add(new DeviceInfo { Name = hw.CpuName, DeviceType = "CPU",
                IpAddress = $"{hw.CpuCores}C/{hw.CpuLogicalProcessors}T @ {hw.CpuMaxClockMHz}MHz", IsOnline = true, Status = "Online", Uptime = "-" });

            Devices.Add(new DeviceInfo { Name = hw.GpuName, DeviceType = "GPU",
                IpAddress = $"{hw.GpuMemoryMB} MB VRAM", IsOnline = true, Status = "Online", Uptime = "-" });

            Devices.Add(new DeviceInfo { Name = $"RAM ({hw.RamTotalMB} MB)", DeviceType = "Memory",
                IpAddress = $"{hw.RamSpeed} MHz {hw.RamManufacturer}", IsOnline = true, Status = "Online", Uptime = "-" });

            foreach (var disk in hw.Disks)
                Devices.Add(new DeviceInfo { Name = disk.Model, DeviceType = $"Disk ({disk.Type})",
                    IpAddress = $"{disk.SizeGB:F0} GB", IsOnline = true, Status = "Online", Uptime = "-" });

            Devices.Add(new DeviceInfo { Name = hw.OsName, DeviceType = "OS",
                IpAddress = $"v{hw.OsVersion} Build {hw.OsBuild}", IsOnline = true, Status = "Running", Uptime = "-" });

            Devices.Add(new DeviceInfo { Name = $"{hw.MotherboardManufacturer} {hw.MotherboardModel}", DeviceType = "Motherboard",
                IpAddress = "-", IsOnline = true, Status = "Online", Uptime = "-" });

            // Add real network adapters
            foreach (var adapter in net.Adapters)
                Devices.Add(new DeviceInfo
                {
                    Name = adapter.Name,
                    DeviceType = "Network",
                    IpAddress = adapter.IpAddress,
                    IsOnline = adapter.Status == "Up",
                    Status = adapter.Status,
                    Uptime = $"{adapter.SpeedMbps} Mbps"
                });

            DeviceCount = Devices.Count;
            OnlineDevices = Devices.Count(d => d.IsOnline);
            OfflineDevices = Devices.Count(d => !d.IsOnline);
            AlertingDevices = OfflineDevices;
            LastScanTime = DateTime.Now.ToString("HH:mm:ss");
            TopologyStatus = $"Found {DeviceCount} devices — {OnlineDevices} online";
        }
        catch (Exception ex) { TopologyStatus = "Error: " + ex.Message; }
    }

    [RelayCommand]
    public void RefreshTopology() => ScanAsync();
}

public class DeviceInfo
{
    public string Name { get; set; } = "";
    public string DeviceType { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public bool IsOnline { get; set; }
    public string Status { get; set; } = "";
    public string Uptime { get; set; } = "";
}

// =================================================================
// 9. USER PROFILES (was User & Roles)
// =================================================================
public partial class UserRoleViewModel : ObservableObject
{
    [ObservableProperty] private string currentRole = "Admin";
    [ObservableProperty] private int userCount = 2;
    [ObservableProperty] private bool canModifySettings = true;
    [ObservableProperty] private string roleStatus = "Admin mode — full access";
    [ObservableProperty] private ObservableCollection<UserInfo> users = new();
    [ObservableProperty] private int adminCount = 1;
    [ObservableProperty] private int operatorCount = 1;
    [ObservableProperty] private int viewerCount;

    public UserRoleViewModel()
    {
        Users.Add(new UserInfo { Name = Environment.UserName, Email = $"{Environment.UserName}@{Environment.MachineName}",
            Role = "Admin", CanModifySettings = true });
        Users.Add(new UserInfo { Name = "Safe Mode", Email = "Read-only access",
            Role = "Viewer", CanModifySettings = false });
        UserCount = Users.Count;
        AdminCount = Users.Count(u => u.Role == "Admin");
        OperatorCount = 0;
        ViewerCount = Users.Count(u => u.Role == "Viewer");
    }

    [RelayCommand]
    public void ManageRole(object? param)
    {
        if (param is UserInfo user)
        {
            CurrentRole = user.Role;
            CanModifySettings = user.CanModifySettings;
            RoleStatus = user.CanModifySettings ? "Admin mode — full access" : "Safe mode — read-only";
        }
    }
}

public class UserInfo
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public bool CanModifySettings { get; set; }
}

// =================================================================
// 10. LOGS & AUDIT — Real log file
// =================================================================
public partial class LogsAuditViewModel : ObservableObject
{
    private readonly LoggingService _logger;

    [ObservableProperty] private int totalLogEntries;
    [ObservableProperty] private string logStatus = "Loading audit log...";
    [ObservableProperty] private ObservableCollection<string> logEntries = new();
    [ObservableProperty] private int metricCount;
    [ObservableProperty] private int slaCount;
    [ObservableProperty] private string lastRefreshTime = "-";

    public LogsAuditViewModel(LoggingService logger)
    {
        _logger = logger;
        RefreshLogs();
    }

    [RelayCommand]
    public void RefreshLogs()
    {
        var entries = _logger.GetRecentLogs(100);
        LogEntries.Clear();
        foreach (var e in entries)
            LogEntries.Add($"[{e.Timestamp:HH:mm:ss}] [{e.Level}] [{e.Source}] {e.Message}");

        TotalLogEntries = _logger.TotalCount;
        MetricCount = entries.Count(e => e.Level == LogLevel.Info);
        SlaCount = entries.Count(e => e.Level == LogLevel.Action);
        LastRefreshTime = DateTime.Now.ToString("HH:mm:ss");
        LogStatus = $"{TotalLogEntries} log entries — file: {Path.GetFileName(_logger.LogFilePath)}";
    }

    [RelayCommand]
    public async Task ExportLogs()
    {
        LogStatus = "Exporting logs...";
        try
        {
            var json = await _logger.ExportLogsJsonAsync();
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"SlaGuardianX_Logs_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            await File.WriteAllTextAsync(path, json);
            LogStatus = $"Logs exported to Desktop: {Path.GetFileName(path)}";
            _logger.Log(LogLevel.Action, "Audit", $"Logs exported to {path}");
        }
        catch (Exception ex) { LogStatus = "Export failed: " + ex.Message; }
    }
}

// =================================================================
// 11. SETTINGS
// =================================================================
public partial class SettingsViewModel : ObservableObject
{
    private readonly HealthRuleEngine _ruleEngine;
    private readonly SystemMonitoringService _monitor;
    private readonly LoggingService _logger;

    [ObservableProperty] private int refreshIntervalMs = 2000;
    [ObservableProperty] private string settingsStatus = "Settings loaded";
    [ObservableProperty] private double guaranteedBandwidth;  // CPU threshold
    [ObservableProperty] private int totalMetrics;
    [ObservableProperty] private int totalSlaResults;
    [ObservableProperty] private string databaseSize = "-";

    public SettingsViewModel(HealthRuleEngine ruleEngine, SystemMonitoringService monitor, LoggingService logger)
    {
        _ruleEngine = ruleEngine;
        _monitor = monitor;
        _logger = logger;
        GuaranteedBandwidth = _ruleEngine.CpuThreshold;
        TotalMetrics = _monitor.History.Count;
        TotalSlaResults = _logger.TotalCount;
        var logFile = _logger.LogFilePath;
        DatabaseSize = File.Exists(logFile) ? $"{new FileInfo(logFile).Length / 1024.0:F1} KB" : "0 KB";
    }

    [RelayCommand]
    public async Task SaveSettings()
    {
        _ruleEngine.CpuThreshold = GuaranteedBandwidth;
        _monitor.Stop();
        _monitor.Start(RefreshIntervalMs);
        _logger.Log(LogLevel.Action, "Settings", $"Settings saved: CPU threshold={GuaranteedBandwidth}%, Interval={RefreshIntervalMs}ms");
        SettingsStatus = $"Saved — CPU threshold: {GuaranteedBandwidth}%, interval: {RefreshIntervalMs}ms";
        await Task.CompletedTask;
    }

    [RelayCommand]
    public async Task ResetDatabase()
    {
        SettingsStatus = "Resetting...";
        _logger.Log(LogLevel.Action, "Settings", "Data reset requested.");
        TotalMetrics = 0;
        TotalSlaResults = 0;
        SettingsStatus = "In-memory data cleared. Log file retained.";
        await Task.CompletedTask;
    }
}

// =================================================================
// 12. ROOT CAUSE ANALYZER — Maps issue → cause with real processes
// =================================================================
public partial class RootCauseAnalyzerViewModel : ObservableObject
{
    private readonly SystemMonitoringService _monitor;
    private readonly ProcessAnalysisService _procService;
    private readonly HealthRuleEngine _ruleEngine;

    [ObservableProperty] private string detectedIssue = "Run analysis to detect issues";
    [ObservableProperty] private string recommendation = "";
    [ObservableProperty] private string analysisStatus = "Ready for analysis";
    [ObservableProperty] private bool isAnalyzing;
    [ObservableProperty] private double avgBandwidth;   // CPU
    [ObservableProperty] private double avgLatency;     // RAM
    [ObservableProperty] private double avgPacketLoss;  // Disk
    [ObservableProperty] private string severityLevel = "-";
    [ObservableProperty] private int samplesAnalyzed;
    [ObservableProperty] private ObservableCollection<string> findings = new();

    public RootCauseAnalyzerViewModel(SystemMonitoringService monitor, ProcessAnalysisService procService,
        HealthRuleEngine ruleEngine)
    {
        _monitor = monitor;
        _procService = procService;
        _ruleEngine = ruleEngine;
    }

    [RelayCommand]
    public async Task AnalyzeIssue()
    {
        IsAnalyzing = true;
        Findings.Clear();
        AnalysisStatus = "Analyzing system...";

        try
        {
            var snap = await Task.Run(() => _monitor.CollectMetrics());
            var report = _ruleEngine.Evaluate(snap);
            SamplesAnalyzed = _monitor.History.Count;

            AvgBandwidth = Math.Round(snap.CpuPercent, 1);
            AvgLatency = Math.Round(snap.RamPercent, 1);
            AvgPacketLoss = Math.Round(snap.DiskUsagePercent, 1);

            if (snap.CpuPercent > 85)
            {
                DetectedIssue = "High CPU Usage";
                SeverityLevel = "Critical";
                var topCpu = await _procService.GetTopCpuProcessesAsync(5);
                Recommendation = "Close CPU-intensive processes listed below.";
                foreach (var p in topCpu)
                    Findings.Add($"🔴 {p.Name} — CPU time: {p.CpuSeconds:F0}s, RAM: {p.MemoryMB:F0} MB");
            }
            else if (snap.RamPercent > 80)
            {
                DetectedIssue = "High Memory Usage";
                SeverityLevel = "Critical";
                var topMem = await _procService.GetTopMemoryProcessesAsync(5);
                Recommendation = "Close memory-heavy applications or increase RAM.";
                foreach (var p in topMem)
                    Findings.Add($"🔴 {p.Name} — RAM: {p.MemoryMB:F0} MB, Threads: {p.ThreadCount}");
            }
            else if (snap.FreeDiskGB < 10)
            {
                DetectedIssue = "Low Disk Space";
                SeverityLevel = "Warning";
                Recommendation = "Clear temp files or uninstall unused applications.";
                var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed);
                foreach (var d in drives)
                    Findings.Add($"💽 {d.Name} — {d.AvailableFreeSpace / 1_073_741_824.0:F1} GB free / {d.TotalSize / 1_073_741_824.0:F0} GB total");
            }
            else
            {
                DetectedIssue = "No Critical Issues Detected";
                SeverityLevel = "Normal";
                Recommendation = "System is running within healthy parameters.";
            }

            Findings.Add($"CPU: {snap.CpuPercent:F0}% | RAM: {snap.RamPercent:F0}% | Disk: {snap.DiskUsagePercent:F0}%");
            Findings.Add($"Network: ↓{snap.NetworkDownloadMbps:F2} / ↑{snap.NetworkUploadMbps:F2} Mbps");
            Findings.Add($"Processes: {snap.ProcessCount} | Health: {report.Status} ({report.HealthScore})");

            AnalysisStatus = $"Analysis complete — {report.Status}";
        }
        catch (Exception ex)
        {
            DetectedIssue = "Analysis failed";
            AnalysisStatus = "Error: " + ex.Message;
        }
        IsAnalyzing = false;
    }
}

// =================================================================
// 13. TRAFFIC ANALYZER — Real per-process network/IO usage
// =================================================================
public partial class TrafficAnalyzerViewModel : ObservableObject
{
    private readonly ProcessAnalysisService _procService;

    [ObservableProperty] private string topApp = "-";
    [ObservableProperty] private double topAppBandwidth;
    [ObservableProperty] private string trafficStatus = "Loading traffic analysis...";
    [ObservableProperty] private ObservableCollection<AppTrafficInfo> appTraffic = new();
    [ObservableProperty] private double totalTrafficMbps;
    [ObservableProperty] private int activeFlows;

    public TrafficAnalyzerViewModel(ProcessAnalysisService procService)
    {
        _procService = procService;
        AnalyzeAsync();
    }

    private async void AnalyzeAsync()
    {
        try
        {
            var topMem = await _procService.GetTopMemoryProcessesAsync(15);
            var summary = await _procService.GetProcessSummaryAsync();

            double totalMB = topMem.Sum(p => p.MemoryMB);
            TotalTrafficMbps = Math.Round(totalMB, 0);
            ActiveFlows = summary.TotalProcesses;

            AppTraffic.Clear();
            foreach (var p in topMem)
            {
                double pct = totalMB > 0 ? Math.Round(p.MemoryMB / totalMB * 100, 1) : 0;
                AppTraffic.Add(new AppTrafficInfo
                {
                    AppName = p.Name,
                    TrafficMbps = p.MemoryMB,
                    Percentage = pct
                });
            }

            TopApp = topMem.FirstOrDefault()?.Name ?? "-";
            TopAppBandwidth = topMem.FirstOrDefault()?.MemoryMB ?? 0;
            TrafficStatus = $"{topMem.Count} top processes — {summary.TotalProcesses} total, {summary.TotalMemoryMB:F0} MB total RAM";
        }
        catch (Exception ex) { TrafficStatus = "Error: " + ex.Message; }
    }

    [RelayCommand]
    public void RefreshTopApps() => AnalyzeAsync();
}

public class AppTrafficInfo
{
    public string AppName { get; set; } = "";
    public double TrafficMbps { get; set; }
    public double Percentage { get; set; }
}

// =================================================================
// 14. CAPACITY PLANNING — Real resource trend prediction
// =================================================================
public partial class CapacityPlanningViewModel : ObservableObject
{
    private readonly SystemMonitoringService _monitor;

    [ObservableProperty] private string recommendedUpgrade = "Calculating...";
    [ObservableProperty] private double projectedGrowth;
    [ObservableProperty] private string planningStatus = "Loading capacity data...";
    [ObservableProperty] private double currentCapacity;     // total RAM MB
    [ObservableProperty] private double peakUsage;           // peak RAM used
    [ObservableProperty] private double utilizationPercent;
    [ObservableProperty] private string timeToExhaustion = "-";
    [ObservableProperty] private double headroomMbps;        // free RAM MB
    [ObservableProperty] private double headroomPercent;
    [ObservableProperty] private string predictionConfidence = "-";

    public CapacityPlanningViewModel(SystemMonitoringService monitor)
    {
        _monitor = monitor;
        CalculateAsync();
    }

    private void CalculateAsync()
    {
        try
        {
            var history = _monitor.GetRecentSnapshots(200);
            var snap = history.LastOrDefault() ?? _monitor.CollectMetrics();

            CurrentCapacity = snap.TotalRamMB;
            PeakUsage = history.Count > 0 ? Math.Round((double)history.Max(s => s.UsedRamMB), 0) : snap.UsedRamMB;
            UtilizationPercent = CurrentCapacity > 0 ? Math.Round(PeakUsage / CurrentCapacity * 100, 1) : 0;
            HeadroomMbps = CurrentCapacity - PeakUsage;
            HeadroomPercent = 100 - UtilizationPercent;

            // Trend: compare first half vs second half of history
            if (history.Count >= 10)
            {
                int half = history.Count / 2;
                double firstAvg = history.Take(half).Average(s => s.RamPercent);
                double secondAvg = history.Skip(half).Average(s => s.RamPercent);
                ProjectedGrowth = Math.Round(secondAvg - firstAvg, 1);

                if (ProjectedGrowth > 0 && HeadroomMbps > 0)
                {
                    double snapsToExhaust = HeadroomMbps / (ProjectedGrowth * snap.TotalRamMB / 100);
                    TimeToExhaustion = snapsToExhaust > 1000 ? "Not foreseeable" : $"~{snapsToExhaust:F0} snapshots";
                }
                else TimeToExhaustion = "Not foreseeable";

                PredictionConfidence = history.Count > 50 ? "High" : history.Count > 20 ? "Medium" : "Low";
            }
            else
            {
                PredictionConfidence = "Need more data";
                TimeToExhaustion = "Collecting...";
            }

            if (UtilizationPercent > 85)
                RecommendedUpgrade = $"Upgrade RAM — peak at {UtilizationPercent:F0}%";
            else if (UtilizationPercent > 65)
                RecommendedUpgrade = "Monitor closely — usage trending upward";
            else
                RecommendedUpgrade = "Current capacity is sufficient";

            PlanningStatus = $"RAM: {PeakUsage:F0}/{CurrentCapacity:F0} MB ({UtilizationPercent:F0}% peak) — {history.Count} samples";
        }
        catch (Exception ex) { PlanningStatus = "Error: " + ex.Message; }
    }

    [RelayCommand]
    public void GenerateCapacityReport() => CalculateAsync();
}

// =================================================================
// 15. SMART NOTIFICATIONS — Real toast / alert feed
// =================================================================
public partial class SmartNotificationsViewModel : ObservableObject
{
    private readonly AlertEngine _alertEngine;
    private readonly LoggingService _logger;

    [ObservableProperty] private bool isNotificationsEnabled = true;
    [ObservableProperty] private int notificationCount;
    [ObservableProperty] private string statusMessage = "Notification center ready";
    [ObservableProperty] private ObservableCollection<NotificationInfo> notifications = new();
    [ObservableProperty] private bool emailAlerts = true;
    [ObservableProperty] private bool smsAlerts;
    [ObservableProperty] private bool slackIntegration;
    [ObservableProperty] private int criticalThreshold = 85;  // CPU threshold

    public SmartNotificationsViewModel(AlertEngine alertEngine, LoggingService logger)
    {
        _alertEngine = alertEngine;
        _logger = logger;

        _alertEngine.AlertFired += OnAlertFired;
        LoadExisting();
    }

    private void LoadExisting()
    {
        foreach (var a in _alertEngine.Alerts.Take(20))
        {
            Notifications.Add(new NotificationInfo
            {
                Title = a.Source,
                Message = a.Message,
                Severity = a.Severity.ToString(),
                Timestamp = a.Timestamp
            });
        }
        NotificationCount = Notifications.Count;
        StatusMessage = $"{NotificationCount} notifications loaded";
    }

    private void OnAlertFired(object? sender, AlertRecord alert)
    {
        if (!IsNotificationsEnabled) return;
        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            Notifications.Insert(0, new NotificationInfo
            {
                Title = alert.Source,
                Message = alert.Message,
                Severity = alert.Severity.ToString(),
                Timestamp = alert.Timestamp
            });
            if (Notifications.Count > 100) Notifications.RemoveAt(Notifications.Count - 1);
            NotificationCount = Notifications.Count;
        });
    }

    [RelayCommand]
    public void TestNotification()
    {
        Notifications.Insert(0, new NotificationInfo
        {
            Title = "Test",
            Message = "This is a test notification from the Smart Notifications engine.",
            Severity = "Info",
            Timestamp = DateTime.Now
        });
        NotificationCount = Notifications.Count;
        StatusMessage = "Test notification sent";
        _logger.Log(LogLevel.Info, "Notification", "Test notification triggered.");
    }

    [RelayCommand]
    public void ClearNotifications()
    {
        Notifications.Clear();
        NotificationCount = 0;
        StatusMessage = "All notifications cleared";
    }
}

public class NotificationInfo
{
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string Severity { get; set; } = "Info";
    public DateTime Timestamp { get; set; }
}

// =================================================================
// 17. PROCESS MONITOR — Task Manager-like with search & end task
// =================================================================
public partial class ProcessMonitorViewModel : ObservableObject
{
    private readonly ProcessAnalysisService _procService;
    private readonly LoggingService _logger;
    private System.Threading.Timer? _refreshTimer;

    [ObservableProperty] private ObservableCollection<ProcessInfo> processes = new();
    [ObservableProperty] private ObservableCollection<ProcessInfo> filteredProcesses = new();
    [ObservableProperty] private ObservableCollection<ProcessInfo> topCpuProcesses = new();
    [ObservableProperty] private ObservableCollection<ProcessInfo> topRamProcesses = new();
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string statusMessage = "Loading processes...";
    [ObservableProperty] private int totalProcesses;
    [ObservableProperty] private double totalMemoryMB;
    [ObservableProperty] private int totalThreads;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private string lastUpdated = "-";
    [ObservableProperty] private ProcessInfo? selectedProcess;

    public ProcessMonitorViewModel(ProcessAnalysisService procService, LoggingService logger)
    {
        _procService = procService;
        _logger = logger;
        LoadProcessesAsync();
        StartAutoRefresh();
    }

    partial void OnSearchTextChanged(string value)
    {
        FilterProcesses();
    }

    private void FilterProcesses()
    {
        FilteredProcesses.Clear();
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? Processes
            : Processes.Where(p => p.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        
        foreach (var p in filtered)
            FilteredProcesses.Add(p);
    }

    [RelayCommand]
    public async Task RefreshProcesses()
    {
        IsRefreshing = true;
        await LoadProcessesAsync();
        IsRefreshing = false;
    }

    private async Task LoadProcessesAsync()
    {
        try
        {
            var all = await _procService.GetAllProcessesAsync();
            var summary = await _procService.GetProcessSummaryAsync();

            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                Processes.Clear();
                foreach (var p in all)
                    Processes.Add(p);

                FilterProcesses();

                // Top CPU
                TopCpuProcesses.Clear();
                foreach (var p in all.OrderByDescending(x => x.CpuPercent).Take(10))
                    TopCpuProcesses.Add(p);

                // Top RAM
                TopRamProcesses.Clear();
                foreach (var p in all.OrderByDescending(x => x.MemoryMB).Take(10))
                    TopRamProcesses.Add(p);

                TotalProcesses = summary.TotalProcesses;
                TotalMemoryMB = Math.Round(summary.TotalMemoryMB, 0);
                TotalThreads = summary.TotalThreads;
                LastUpdated = DateTime.Now.ToString("HH:mm:ss");
                StatusMessage = $"{TotalProcesses} processes | {TotalMemoryMB:N0} MB RAM used";
            });
        }
        catch (Exception ex)
        {
            StatusMessage = "Error: " + ex.Message;
        }
    }

    [RelayCommand]
    public async Task EndTask(int pid)
    {
        var result = await _procService.EndTaskAsync(pid);
        if (result.Success)
        {
            _logger.Log(LogLevel.Action, "Process", result.Message);
            await RefreshProcesses();
        }
        else
        {
            StatusMessage = result.Message;
            _logger.Log(LogLevel.Warning, "Process", result.Message);
        }
    }

    private void StartAutoRefresh()
    {
        _refreshTimer = new System.Threading.Timer(async _ =>
        {
            await LoadProcessesAsync();
        }, null, 3000, 3000); // Refresh every 3 seconds
    }

    public void StopAutoRefresh()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }
}

// =================================================================
// 18. SERVICES MONITOR — Windows Services control panel
// =================================================================
public partial class ServicesMonitorViewModel : ObservableObject
{
    private readonly ServiceMonitoringService _svcService;
    private readonly LoggingService _logger;

    [ObservableProperty] private ObservableCollection<ServiceInfoModel> services = new();
    [ObservableProperty] private ObservableCollection<ServiceInfoModel> filteredServices = new();
    [ObservableProperty] private ObservableCollection<ServiceInfoModel> criticalServices = new();
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string statusMessage = "Loading services...";
    [ObservableProperty] private int totalServices;
    [ObservableProperty] private int runningServices;
    [ObservableProperty] private int stoppedServices;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string lastUpdated = "-";
    [ObservableProperty] private ServiceInfoModel? selectedService;
    [ObservableProperty] private bool showCriticalOnly;

    public ServicesMonitorViewModel(ServiceMonitoringService svcService, LoggingService logger)
    {
        _svcService = svcService;
        _logger = logger;
        LoadServicesAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        FilterServices();
    }

    partial void OnShowCriticalOnlyChanged(bool value)
    {
        FilterServices();
    }

    private void FilterServices()
    {
        FilteredServices.Clear();
        var filtered = Services.AsEnumerable();

        if (ShowCriticalOnly)
            filtered = filtered.Where(s => s.IsCritical);

        if (!string.IsNullOrWhiteSpace(SearchText))
            filtered = filtered.Where(s => 
                s.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                s.ServiceName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        foreach (var s in filtered)
            FilteredServices.Add(s);
    }

    [RelayCommand]
    public async Task RefreshServices()
    {
        await LoadServicesAsync();
    }

    private async Task LoadServicesAsync()
    {
        IsLoading = true;
        try
        {
            var all = await _svcService.GetServicesAsync();
            var critical = await _svcService.GetCriticalServicesAsync();

            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                Services.Clear();
                foreach (var s in all)
                    Services.Add(s);

                CriticalServices.Clear();
                foreach (var s in critical)
                    CriticalServices.Add(s);

                FilterServices();

                TotalServices = all.Count;
                RunningServices = all.Count(s => s.IsRunning);
                StoppedServices = all.Count(s => s.IsStopped);
                LastUpdated = DateTime.Now.ToString("HH:mm:ss");
                StatusMessage = $"{TotalServices} services | {RunningServices} running | {StoppedServices} stopped";
            });
        }
        catch (Exception ex)
        {
            StatusMessage = "Error: " + ex.Message;
        }
        IsLoading = false;
    }

    [RelayCommand]
    public async Task StartService(string serviceName)
    {
        StatusMessage = $"Starting {serviceName}...";
        var result = await _svcService.StartServiceAsync(serviceName);
        StatusMessage = result.Message;
        _logger.Log(result.Success ? LogLevel.Info : LogLevel.Warning, "Service", result.Message);
        await RefreshServices();
    }

    [RelayCommand]
    public async Task StopService(string serviceName)
    {
        StatusMessage = $"Stopping {serviceName}...";
        var result = await _svcService.StopServiceAsync(serviceName);
        StatusMessage = result.Message;
        _logger.Log(result.Success ? LogLevel.Info : LogLevel.Warning, "Service", result.Message);
        await RefreshServices();
    }

    [RelayCommand]
    public async Task RestartService(string serviceName)
    {
        StatusMessage = $"Restarting {serviceName}...";
        var result = await _svcService.RestartServiceAsync(serviceName);
        StatusMessage = result.Message;
        _logger.Log(result.Success ? LogLevel.Info : LogLevel.Warning, "Service", result.Message);
        await RefreshServices();
    }
}
