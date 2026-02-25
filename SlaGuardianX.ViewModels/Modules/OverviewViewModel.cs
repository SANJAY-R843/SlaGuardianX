using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlaGuardianX.Services;
using System.Collections.ObjectModel;

namespace SlaGuardianX.ViewModels.Modules;

// ═══════════════════════════════════════════════════════════
// OVERVIEW DASHBOARD — Real system telemetry aggregate
// ═══════════════════════════════════════════════════════════
public partial class OverviewViewModel : ObservableObject
{
    private readonly SystemMonitoringService _monitor;
    private readonly HealthRuleEngine _healthEngine;
    private readonly AlertEngine _alertEngine;
    private readonly LoggingService _logger;

    [ObservableProperty] private double currentBandwidth;
    [ObservableProperty] private double slaCompliancePercentage = 100;
    [ObservableProperty] private double riskScore;
    [ObservableProperty] private double predictedBandwidth;
    [ObservableProperty] private double currentLatency;
    [ObservableProperty] private double currentPacketLoss;
    [ObservableProperty] private double currentUptime;
    [ObservableProperty] private string optimizationStatus = "Idle";
    [ObservableProperty] private int activeAlertCount;
    [ObservableProperty] private string statusMessage = "Initializing system scan...";
    [ObservableProperty] private string lastUpdated = "-";
    [ObservableProperty] private int totalDataPoints;

    public OverviewViewModel(SystemMonitoringService monitor, HealthRuleEngine healthEngine,
        AlertEngine alertEngine, LoggingService logger)
    {
        _monitor = monitor;
        _healthEngine = healthEngine;
        _alertEngine = alertEngine;
        _logger = logger;

        _monitor.SnapshotCaptured += OnSnapshot;
        _alertEngine.AlertFired += (_, _) =>
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                ActiveAlertCount = _alertEngine.Alerts.Count;
            });
        };

        if (!_monitor.IsMonitoring)
            _monitor.Start(2000);

        LoadInitialAsync();
    }

    private async void LoadInitialAsync()
    {
        try
        {
            var snap = await Task.Run(() => _monitor.CollectMetrics());
            ApplySnapshot(snap);
            _logger.Log(LogLevel.Info, "Overview", "Initial system scan completed.");
        }
        catch (Exception ex) { StatusMessage = "Error: " + ex.Message; }
    }

    private void OnSnapshot(object? sender, SystemSnapshot snap)
    {
        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            ApplySnapshot(snap);
            _alertEngine.ProcessSnapshot(snap);
        });
    }

    private void ApplySnapshot(SystemSnapshot snap)
    {
        // Map real metrics to card bindings
        CurrentBandwidth = snap.NetworkDownloadMbps;           // Download speed
        CurrentLatency = Math.Round(snap.CpuPercent, 1);       // CPU %
        CurrentPacketLoss = Math.Round(snap.RamPercent, 1);    // RAM %
        CurrentUptime = Math.Round(snap.DiskUsagePercent, 1);  // Disk used %
        PredictedBandwidth = snap.NetworkUploadMbps;           // Upload speed

        // Health
        var report = _healthEngine.Evaluate(snap);
        SlaCompliancePercentage = report.HealthScore;
        RiskScore = 100 - report.HealthScore;
        ActiveAlertCount = _alertEngine.Alerts.Count;

        TotalDataPoints++;
        LastUpdated = DateTime.Now.ToString("HH:mm:ss");

        OptimizationStatus = report.Status == HealthStatus.Healthy ? "All Clear" :
                             report.Status == HealthStatus.Warning ? "Attention" : "Action Required";

        StatusMessage = $"CPU {snap.CpuPercent:F0}% | RAM {snap.RamPercent:F0}% | Disk {snap.DiskUsagePercent:F0}% | ↓{snap.NetworkDownloadMbps:F1} Mbps | {report.Status}";
    }

    [RelayCommand]
    public void StartMonitoring()
    {
        _monitor.Start(2000);
        _logger.Log(LogLevel.Action, "Overview", "Monitoring started.");
        StatusMessage = "Live monitoring active";
    }

    [RelayCommand]
    public void StopMonitoring()
    {
        _monitor.Stop();
        _logger.Log(LogLevel.Action, "Overview", "Monitoring stopped.");
        StatusMessage = "Monitoring paused";
    }

    [RelayCommand]
    public async Task RefreshMetrics()
    {
        var snap = await Task.Run(() => _monitor.CollectMetrics());
        ApplySnapshot(snap);
    }
}
