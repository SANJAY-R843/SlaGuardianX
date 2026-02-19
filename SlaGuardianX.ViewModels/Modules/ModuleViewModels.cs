using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlaGuardianX.Services;
using SlaGuardianX.Data;
using SlaGuardianX.Models;
using System.Collections.ObjectModel;

namespace SlaGuardianX.ViewModels.Modules;

// =================================================================
// 1. REAL-TIME MONITORING
// =================================================================
public partial class RealTimeMonitoringViewModel : ObservableObject
{
    private readonly TrafficSimulatorService _trafficService;
    private readonly SlaService _slaService;

    [ObservableProperty] private double bandwidth;
    [ObservableProperty] private double latency;
    [ObservableProperty] private double packetLoss;
    [ObservableProperty] private double jitter;
    [ObservableProperty] private double uptime;
    [ObservableProperty] private bool isMonitoring;
    [ObservableProperty] private string statusMessage = "Idle - press Start to begin monitoring";
    [ObservableProperty] private int dataPointsCollected;
    [ObservableProperty] private double peakBandwidth;
    [ObservableProperty] private double minBandwidth = 999;
    [ObservableProperty] private double avgBandwidth;
    [ObservableProperty] private string lastUpdated = "-";
    [ObservableProperty] private ObservableCollection<string> recentEvents = new();

    private double _bandwidthSum;

    public RealTimeMonitoringViewModel(TrafficSimulatorService trafficService, SlaService slaService)
    {
        _trafficService = trafficService;
        _slaService = slaService;
        _trafficService.MetricGenerated += OnMetricGenerated;
        LoadInitialDataAsync();
    }

    private async void LoadInitialDataAsync()
    {
        try
        {
            var metrics = await _trafficService.GetRecentMetricsAsync(10);
            if (metrics.Count > 0)
            {
                var latest = metrics.Last();
                Bandwidth = latest.Bandwidth;
                Latency = latest.Latency;
                PacketLoss = latest.PacketLoss;
                Uptime = latest.Uptime;
                Jitter = Math.Abs(latest.Latency - (metrics.Count > 1 ? metrics[metrics.Count - 2].Latency : latest.Latency));
                DataPointsCollected = await _trafficService.GetMetricCountAsync();
                StatusMessage = "Loaded " + metrics.Count + " recent data points";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "Error loading data: " + ex.Message;
        }
    }

    private void OnMetricGenerated(object? sender, NetworkMetric metric)
    {
        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            Bandwidth = Math.Round(metric.Bandwidth, 2);
            Latency = Math.Round(metric.Latency, 1);
            PacketLoss = Math.Round(metric.PacketLoss, 3);
            Uptime = Math.Round(metric.Uptime, 2);
            Jitter = Math.Round(Math.Abs(metric.Latency - Latency), 1);
            DataPointsCollected++;
            LastUpdated = DateTime.Now.ToString("HH:mm:ss");

            _bandwidthSum += metric.Bandwidth;
            AvgBandwidth = Math.Round(_bandwidthSum / Math.Max(1, DataPointsCollected), 2);
            if (metric.Bandwidth > PeakBandwidth) PeakBandwidth = Math.Round(metric.Bandwidth, 2);
            if (metric.Bandwidth < MinBandwidth) MinBandwidth = Math.Round(metric.Bandwidth, 2);

            if (metric.PacketLoss > 2)
                AddEvent("High packet loss: " + metric.PacketLoss.ToString("F2") + "%");
            if (metric.Latency > 100)
                AddEvent("High latency: " + metric.Latency.ToString("F0") + "ms");
            if (metric.Bandwidth < _slaService.GuaranteedBandwidth)
                AddEvent("SLA breach: " + metric.Bandwidth.ToString("F1") + " < " + _slaService.GuaranteedBandwidth + " Mbps");
        });
    }

    private void AddEvent(string msg)
    {
        RecentEvents.Insert(0, "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg);
        if (RecentEvents.Count > 20) RecentEvents.RemoveAt(RecentEvents.Count - 1);
    }

    [RelayCommand]
    public void StartMonitoring()
    {
        _trafficService.Start();
        IsMonitoring = true;
        StatusMessage = "Live monitoring active - collecting metrics every 2s";
        AddEvent("Monitoring started");
    }

    [RelayCommand]
    public void StopMonitoring()
    {
        _trafficService.Stop();
        IsMonitoring = false;
        StatusMessage = "Monitoring paused";
        AddEvent("Monitoring stopped");
    }

    [RelayCommand]
    public async Task RefreshTelemetry()
    {
        StatusMessage = "Refreshing telemetry...";
        var metrics = await _trafficService.GetRecentMetricsAsync(1);
        if (metrics.Count > 0)
        {
            var m = metrics.Last();
            Bandwidth = Math.Round(m.Bandwidth, 2);
            Latency = Math.Round(m.Latency, 1);
            PacketLoss = Math.Round(m.PacketLoss, 3);
            Uptime = Math.Round(m.Uptime, 2);
            StatusMessage = "Telemetry refreshed";
        }
    }
}

// =================================================================
// 2. SLA MANAGER
// =================================================================
public partial class SlaManagerViewModel : ObservableObject
{
    private readonly SlaService _slaService;
    private readonly TrafficSimulatorService _trafficService;

    [ObservableProperty] private double guaranteedBandwidth;
    [ObservableProperty] private double maxLatency = 100;
    [ObservableProperty] private double maxPacketLoss = 1;
    [ObservableProperty] private int totalViolations;
    [ObservableProperty] private double penaltyEstimate;
    [ObservableProperty] private double compliancePercentage;
    [ObservableProperty] private int totalRecords;
    [ObservableProperty] private string statusMessage = "Loading SLA configuration...";
    [ObservableProperty] private string slaProfileName = "Default Profile";
    [ObservableProperty] private double penaltyPerViolation = 150;

    public SlaManagerViewModel(SlaService slaService, TrafficSimulatorService trafficService)
    {
        _slaService = slaService;
        _trafficService = trafficService;
        GuaranteedBandwidth = _slaService.GuaranteedBandwidth;
        LoadSlaDataAsync();
    }

    private async void LoadSlaDataAsync()
    {
        try
        {
            var results = await _slaService.GetAllResultsAsync();
            var resultList = results.ToList();
            TotalRecords = resultList.Count;
            TotalViolations = resultList.Count(r => r.IsViolated);
            CompliancePercentage = resultList.Count > 0
                ? Math.Round(resultList.Count(r => !r.IsViolated) / (double)resultList.Count * 100, 1)
                : 100;
            PenaltyEstimate = TotalViolations * PenaltyPerViolation;
            StatusMessage = "SLA profile loaded - " + TotalRecords + " records analyzed";
        }
        catch (Exception ex) { StatusMessage = "Error: " + ex.Message; }
    }

    [RelayCommand]
    public async Task SaveSlaProfile()
    {
        _slaService.GuaranteedBandwidth = GuaranteedBandwidth;
        StatusMessage = "SLA profile saved - guaranteed: " + GuaranteedBandwidth + " Mbps";
        await Task.CompletedTask;
    }

    [RelayCommand]
    public async Task CalculatePenalty()
    {
        var results = await _slaService.GetAllResultsAsync();
        var resultList = results.ToList();
        TotalViolations = resultList.Count(r => r.IsViolated);
        PenaltyEstimate = TotalViolations * PenaltyPerViolation;
        CompliancePercentage = resultList.Count > 0
            ? Math.Round(resultList.Count(r => !r.IsViolated) / (double)resultList.Count * 100, 1)
            : 100;
        StatusMessage = "Penalty recalculated: $" + PenaltyEstimate.ToString("N0") + " for " + TotalViolations + " violations";
    }

    [RelayCommand]
    public async Task RunSlaCheck()
    {
        StatusMessage = "Running SLA compliance check...";
        var metrics = await _trafficService.GetRecentMetricsAsync(1);
        if (metrics.Count > 0)
        {
            var result = await _slaService.CalculateSlaAsync(metrics.Last());
            CompliancePercentage = Math.Round(result.CompliancePercentage, 1);
            StatusMessage = result.IsViolated
                ? "SLA VIOLATED - compliance at " + result.CompliancePercentage.ToString("F1") + "%"
                : "SLA compliant - " + result.CompliancePercentage.ToString("F1") + "%";
            TotalRecords++;
            if (result.IsViolated) TotalViolations++;
            PenaltyEstimate = TotalViolations * PenaltyPerViolation;
        }
        else
        {
            StatusMessage = "No metric data available. Start monitoring first.";
        }
    }
}

// =================================================================
// 3. AI PREDICTION
// =================================================================
public partial class AiPredictionViewModel : ObservableObject
{
    private readonly PredictionService _predictionService;
    private readonly SlaService _slaService;

    [ObservableProperty] private double bandwidthForecast;
    [ObservableProperty] private string slaBreachPrediction = "Analyzing...";
    [ObservableProperty] private double riskTrend;
    [ObservableProperty] private int aiConfidence;
    [ObservableProperty] private string statusMessage = "Waiting for data...";
    [ObservableProperty] private int dataPointsUsed;
    [ObservableProperty] private double currentBandwidth;
    [ObservableProperty] private string trendDirection = "-";
    [ObservableProperty] private bool isAnalyzing;

    public AiPredictionViewModel(PredictionService predictionService, SlaService slaService)
    {
        _predictionService = predictionService;
        _slaService = slaService;
        RunInitialPrediction();
    }

    private async void RunInitialPrediction() => await RunPredictionInternal();

    private async Task RunPredictionInternal()
    {
        IsAnalyzing = true;
        StatusMessage = "Running AI prediction model...";
        try
        {
            var result = await _predictionService.PredictBandwidthAsync(50);
            if (result.HasValidPrediction)
            {
                BandwidthForecast = Math.Round(result.PredictedBandwidth, 2);
                CurrentBandwidth = Math.Round(result.CurrentBandwidth, 2);
                RiskTrend = Math.Round(result.Trend, 3);
                DataPointsUsed = result.DataPointsUsed;
                TrendDirection = result.Trend > 0.1 ? "Upward" : result.Trend < -0.1 ? "Downward" : "Stable";

                bool willBreach = result.PredictedBandwidth < _slaService.GuaranteedBandwidth;
                SlaBreachPrediction = willBreach ? "SLA BREACH LIKELY" : "No breach predicted";
                AiConfidence = Math.Min(99, Math.Max(50, 70 + result.DataPointsUsed));

                StatusMessage = "Prediction complete - " + result.DataPointsUsed + " data points analyzed";
            }
            else
            {
                SlaBreachPrediction = "Insufficient data";
                StatusMessage = result.Message;
                AiConfidence = 0;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "Prediction error: " + ex.Message;
        }
        IsAnalyzing = false;
    }

    [RelayCommand]
    public async Task RunPrediction() => await RunPredictionInternal();
}

// =================================================================
// 4. OPTIMIZATION CONTROL
// =================================================================
public partial class OptimizationControlViewModel : ObservableObject
{
    private readonly OptimizationService _optimizationService;
    private readonly TrafficSimulatorService _trafficService;
    private readonly SlaService _slaService;

    [ObservableProperty] private bool isSmartQosEnabled;
    [ObservableProperty] private bool isBackgroundLimiterEnabled;
    [ObservableProperty] private double optimizationBoost = OptimizationService.OptimizationBoostFactor * 100;
    [ObservableProperty] private double beforeBandwidth;
    [ObservableProperty] private double afterBandwidth;
    [ObservableProperty] private double improvementMbps;
    [ObservableProperty] private string statusMessage = "Optimization module ready";
    [ObservableProperty] private bool isOptimizationActive;
    [ObservableProperty] private string optimizationStrategy = "QoS Priority + Traffic Shaping";

    public OptimizationControlViewModel(OptimizationService optimizationService,
        TrafficSimulatorService trafficService, SlaService slaService)
    {
        _optimizationService = optimizationService;
        _trafficService = trafficService;
        _slaService = slaService;
        IsOptimizationActive = _optimizationService.IsOptimizationEnabled;
        IsSmartQosEnabled = _optimizationService.IsOptimizationEnabled;
    }

    [RelayCommand]
    public async Task RunOptimization()
    {
        StatusMessage = "Running bandwidth optimization...";
        var metrics = await _trafficService.GetRecentMetricsAsync(1);
        if (metrics.Count > 0)
        {
            BeforeBandwidth = Math.Round(metrics.Last().Bandwidth, 2);
            double optimized = await _optimizationService.EnableOptimizationAsync(BeforeBandwidth);
            AfterBandwidth = Math.Round(optimized, 2);
            ImprovementMbps = Math.Round(AfterBandwidth - BeforeBandwidth, 2);
            IsOptimizationActive = true;
            IsSmartQosEnabled = true;

            var slaResults = await _slaService.GetRecentResultsAsync(1);
            if (slaResults.Count > 0)
                await _optimizationService.UpdateSlaResultWithOptimizationAsync(slaResults.Last(), optimized);

            StatusMessage = "Optimization active - +" + ImprovementMbps.ToString("F1") + " Mbps (" + OptimizationBoost.ToString("F0") + "% boost)";
        }
        else
        {
            StatusMessage = "No metrics available. Start monitoring first.";
        }
    }

    [RelayCommand]
    public async Task DisableOptimization()
    {
        await _optimizationService.DisableOptimizationAsync();
        IsOptimizationActive = false;
        IsSmartQosEnabled = false;
        AfterBandwidth = 0;
        ImprovementMbps = 0;
        StatusMessage = "Optimization disabled";
    }

    [RelayCommand]
    public async Task EnableSmartQos()
    {
        IsSmartQosEnabled = !IsSmartQosEnabled;
        if (IsSmartQosEnabled)
            await RunOptimization();
        else
            await DisableOptimization();
    }

    [RelayCommand]
    public void EnableBackgroundLimiter()
    {
        IsBackgroundLimiterEnabled = !IsBackgroundLimiterEnabled;
        StatusMessage = IsBackgroundLimiterEnabled
            ? "Background traffic limiter enabled"
            : "Background traffic limiter disabled";
    }
}

// =================================================================
// 5. ALERTS & INCIDENTS
// =================================================================
public partial class AlertsIncidentViewModel : ObservableObject
{
    private readonly SlaService _slaService;
    private readonly TrafficSimulatorService _trafficService;

    [ObservableProperty] private int criticalAlerts;
    [ObservableProperty] private int warningAlerts;
    [ObservableProperty] private int infoAlerts;
    [ObservableProperty] private string selectedSeverity = "All";
    [ObservableProperty] private int totalAlerts;
    [ObservableProperty] private string statusMessage = "Loading alert history...";
    [ObservableProperty] private ObservableCollection<string> alertLog = new();
    [ObservableProperty] private string lastIncidentTime = "-";

    public AlertsIncidentViewModel(SlaService slaService, TrafficSimulatorService trafficService)
    {
        _slaService = slaService;
        _trafficService = trafficService;
        _trafficService.MetricGenerated += OnMetricForAlerts;
        AnalyzeExistingAlertsAsync();
    }

    private async void AnalyzeExistingAlertsAsync()
    {
        try
        {
            var results = await _slaService.GetAllResultsAsync();
            var list = results.ToList();
            CriticalAlerts = list.Count(r => r.IsViolated && r.RiskScore > 60);
            WarningAlerts = list.Count(r => r.RiskScore > 30 && r.RiskScore <= 60);
            InfoAlerts = list.Count(r => r.RiskScore <= 30 && r.RiskScore > 0);
            TotalAlerts = CriticalAlerts + WarningAlerts + InfoAlerts;

            var lastViolation = list.Where(r => r.IsViolated).OrderByDescending(r => r.Timestamp).FirstOrDefault();
            LastIncidentTime = lastViolation != null ? lastViolation.Timestamp.ToString("g") : "None";
            StatusMessage = "Analyzed " + list.Count + " records - " + CriticalAlerts + " critical alerts";
        }
        catch (Exception ex) { StatusMessage = "Error: " + ex.Message; }
    }

    private void OnMetricForAlerts(object? sender, NetworkMetric metric)
    {
        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            if (metric.Bandwidth < _slaService.GuaranteedBandwidth)
            {
                CriticalAlerts++;
                TotalAlerts++;
                var msg = "[" + DateTime.Now.ToString("HH:mm:ss") + "] CRITICAL: Bandwidth " + metric.Bandwidth.ToString("F1") + " < " + _slaService.GuaranteedBandwidth + " Mbps";
                AlertLog.Insert(0, msg);
                LastIncidentTime = DateTime.Now.ToString("g");
            }
            else if (metric.PacketLoss > 2)
            {
                WarningAlerts++;
                TotalAlerts++;
                AlertLog.Insert(0, "[" + DateTime.Now.ToString("HH:mm:ss") + "] WARNING: Packet loss at " + metric.PacketLoss.ToString("F2") + "%");
            }
            else if (metric.Latency > 80)
            {
                InfoAlerts++;
                TotalAlerts++;
                AlertLog.Insert(0, "[" + DateTime.Now.ToString("HH:mm:ss") + "] INFO: Latency elevated at " + metric.Latency.ToString("F0") + "ms");
            }
            if (AlertLog.Count > 50) AlertLog.RemoveAt(AlertLog.Count - 1);
        });
    }

    [RelayCommand]
    public void FilterBySeverity(string severity)
    {
        SelectedSeverity = severity;
        StatusMessage = severity == "All"
            ? "Showing all " + TotalAlerts + " alerts"
            : "Filtered to " + severity + " alerts";
    }

    [RelayCommand]
    public void ClearAlerts()
    {
        AlertLog.Clear();
        CriticalAlerts = 0;
        WarningAlerts = 0;
        InfoAlerts = 0;
        TotalAlerts = 0;
        StatusMessage = "All alerts cleared";
    }
}

// =================================================================
// 6. ANALYTICS & REPORTS
// =================================================================
public partial class AnalyticsReportsViewModel : ObservableObject
{
    private readonly SlaService _slaService;
    private readonly TrafficSimulatorService _trafficService;

    [ObservableProperty] private double slaComplianceTrend;
    [ObservableProperty] private int peakUsageHour;
    [ObservableProperty] private int violationFrequency;
    [ObservableProperty] private double optimizationImpact;
    [ObservableProperty] private string statusMessage = "Loading analytics...";
    [ObservableProperty] private int totalMetrics;
    [ObservableProperty] private double avgBandwidth;
    [ObservableProperty] private double avgLatency;
    [ObservableProperty] private double avgPacketLoss;
    [ObservableProperty] private int optimizedCount;

    public AnalyticsReportsViewModel(SlaService slaService, TrafficSimulatorService trafficService)
    {
        _slaService = slaService;
        _trafficService = trafficService;
        LoadAnalyticsAsync();
    }

    private async void LoadAnalyticsAsync()
    {
        try
        {
            var results = (await _slaService.GetAllResultsAsync()).ToList();
            var metrics = await _trafficService.GetRecentMetricsAsync(200);

            TotalMetrics = metrics.Count;
            if (metrics.Count > 0)
            {
                AvgBandwidth = Math.Round(metrics.Average(m => m.Bandwidth), 2);
                AvgLatency = Math.Round(metrics.Average(m => m.Latency), 1);
                AvgPacketLoss = Math.Round(metrics.Average(m => m.PacketLoss), 3);

                var hourGroups = metrics.GroupBy(m => m.Timestamp.Hour)
                    .OrderByDescending(g => g.Average(m => m.Bandwidth));
                PeakUsageHour = hourGroups.FirstOrDefault()?.Key ?? 0;
            }

            if (results.Count > 0)
            {
                ViolationFrequency = results.Count(r => r.IsViolated);
                SlaComplianceTrend = Math.Round(results.Count(r => !r.IsViolated) / (double)results.Count * 100, 1);
                OptimizedCount = results.Count(r => r.IsOptimized);
                OptimizationImpact = OptimizedCount > 0
                    ? Math.Round((double)OptimizedCount / results.Count * 100, 1)
                    : 0;
            }
            else
            {
                SlaComplianceTrend = 100;
            }

            StatusMessage = "Analytics loaded - " + TotalMetrics + " metrics, " + results.Count + " SLA records";
        }
        catch (Exception ex) { StatusMessage = "Error: " + ex.Message; }
    }

    [RelayCommand]
    public async Task RefreshAnalytics()
    {
        StatusMessage = "Refreshing analytics...";
        LoadAnalyticsAsync();
        await Task.CompletedTask;
    }

    [RelayCommand]
    public async Task ExportPdf()
    {
        StatusMessage = "Generating PDF report...";
        await Task.Delay(800);
        StatusMessage = "PDF report generated (simulated)";
    }

    [RelayCommand]
    public async Task ExportCsv()
    {
        StatusMessage = "Generating CSV export...";
        await Task.Delay(500);
        StatusMessage = "CSV export generated (simulated)";
    }
}

// =================================================================
// 7. MULTI-SITE VIEW
// =================================================================
public partial class MultiSiteViewModel : ObservableObject
{
    private readonly TrafficSimulatorService _trafficService;
    private readonly SlaService _slaService;

    [ObservableProperty] private string selectedSite = "Chennai (HQ)";
    [ObservableProperty] private double siteBandwidth;
    [ObservableProperty] private double siteCompliance;
    [ObservableProperty] private int locationsCount = 4;
    [ObservableProperty] private string statusMessage = "Select a site to view metrics";
    [ObservableProperty] private double siteLatency;
    [ObservableProperty] private string siteStatus = "Online";
    [ObservableProperty] private int siteDeviceCount;
    [ObservableProperty] private ObservableCollection<string> sites = new()
    {
        "Chennai (HQ)", "Mumbai (DR)", "Bangalore (Branch)", "Hyderabad (Edge)"
    };

    public MultiSiteViewModel(TrafficSimulatorService trafficService, SlaService slaService)
    {
        _trafficService = trafficService;
        _slaService = slaService;
        LoadSiteDataAsync("Chennai (HQ)");
    }

    private async void LoadSiteDataAsync(string site)
    {
        StatusMessage = "Loading metrics for " + site + "...";
        try
        {
            var metrics = await _trafficService.GetRecentMetricsAsync(10);
            var rand = new Random(site.GetHashCode());

            if (metrics.Count > 0)
            {
                double offset = rand.NextDouble() * 10 - 5;
                SiteBandwidth = Math.Round(metrics.Average(m => m.Bandwidth) + offset, 2);
                SiteLatency = Math.Round(metrics.Average(m => m.Latency) + rand.Next(-20, 30), 1);
            }
            else
            {
                SiteBandwidth = 35 + rand.NextDouble() * 20;
                SiteLatency = 20 + rand.NextDouble() * 80;
            }

            SiteCompliance = SiteBandwidth >= _slaService.GuaranteedBandwidth
                ? Math.Round(95 + new Random().NextDouble() * 5, 1)
                : Math.Round(70 + new Random().NextDouble() * 20, 1);
            SiteDeviceCount = 5 + rand.Next(0, 15);
            SiteStatus = SiteBandwidth > 25 ? "Online" : "Degraded";
            StatusMessage = site + " - " + SiteDeviceCount + " devices, " + SiteBandwidth.ToString("F1") + " Mbps";
        }
        catch (Exception ex) { StatusMessage = "Error: " + ex.Message; }
    }

    [RelayCommand]
    public void SelectSite(string site)
    {
        SelectedSite = site;
        LoadSiteDataAsync(site);
    }
}

// =================================================================
// 8. DEVICE & TOPOLOGY
// =================================================================
public partial class TopologyViewModel : ObservableObject
{
    private readonly TrafficSimulatorService _trafficService;

    [ObservableProperty] private string topologyStatus = "Scanning network...";
    [ObservableProperty] private int deviceCount;
    [ObservableProperty] private int alertingDevices;
    [ObservableProperty] private int onlineDevices;
    [ObservableProperty] private int offlineDevices;
    [ObservableProperty] private string lastScanTime = "-";
    [ObservableProperty] private ObservableCollection<DeviceInfo> devices = new();

    public TopologyViewModel(TrafficSimulatorService trafficService)
    {
        _trafficService = trafficService;
        ScanNetworkAsync();
    }

    private async void ScanNetworkAsync()
    {
        TopologyStatus = "Scanning network topology...";
        await Task.Delay(300);

        var rand = new Random();
        var deviceNames = new (string, string, string)[] {
            ("Core-Router-01", "Router", "192.168.1.1"),
            ("Core-Switch-01", "Switch", "192.168.1.2"),
            ("Firewall-01", "Firewall", "192.168.1.3"),
            ("AP-Floor1-01", "Access Point", "192.168.2.10"),
            ("AP-Floor2-01", "Access Point", "192.168.2.20"),
            ("Server-DB-01", "Server", "192.168.10.1"),
            ("Server-App-01", "Server", "192.168.10.2"),
            ("Server-Web-01", "Server", "192.168.10.3"),
            ("NAS-Backup-01", "Storage", "192.168.10.10"),
            ("IP-Phone-GW", "Gateway", "192.168.5.1"),
            ("WAN-Link-01", "WAN", "10.0.0.1"),
            ("Load-Balancer", "Balancer", "192.168.10.100"),
        };

        Devices.Clear();
        foreach (var item in deviceNames)
        {
            bool isOnline = rand.NextDouble() > 0.08;
            Devices.Add(new DeviceInfo
            {
                Name = item.Item1, DeviceType = item.Item2, IpAddress = item.Item3,
                IsOnline = isOnline,
                Status = isOnline ? "Online" : "Offline",
                Uptime = isOnline ? rand.Next(1, 365) + "d " + rand.Next(0, 23) + "h" : "-"
            });
        }

        DeviceCount = Devices.Count;
        OnlineDevices = Devices.Count(d => d.IsOnline);
        OfflineDevices = Devices.Count(d => !d.IsOnline);
        AlertingDevices = OfflineDevices;
        LastScanTime = DateTime.Now.ToString("HH:mm:ss");
        TopologyStatus = OfflineDevices == 0 ? "All systems operational" : OfflineDevices + " device(s) offline";
    }

    [RelayCommand]
    public void RefreshTopology() => ScanNetworkAsync();
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
// 9. USER & ROLES
// =================================================================
public partial class UserRoleViewModel : ObservableObject
{
    [ObservableProperty] private string currentRole = "Admin";
    [ObservableProperty] private int totalUsers = 8;
    [ObservableProperty] private bool canModifySettings = true;
    [ObservableProperty] private string statusMessage = "User management loaded";
    [ObservableProperty] private ObservableCollection<UserInfo> users = new();
    [ObservableProperty] private int adminCount;
    [ObservableProperty] private int operatorCount;
    [ObservableProperty] private int viewerCount;

    public UserRoleViewModel()
    {
        var userList = new (string, string, string, bool)[] {
            ("admin", "Admin", "System Administrator", true),
            ("sanjay.r", "Admin", "Network Architect", true),
            ("ops.team1", "Operator", "NOC Operator L1", true),
            ("ops.team2", "Operator", "NOC Operator L2", true),
            ("ops.team3", "Operator", "NOC Operator L3", false),
            ("viewer.mgmt", "Viewer", "Management Dashboard", true),
            ("viewer.audit", "Viewer", "Audit Compliance", true),
            ("api.service", "Operator", "API Service Account", true),
        };
        foreach (var item in userList)
            Users.Add(new UserInfo { Username = item.Item1, Role = item.Item2, Description = item.Item3, IsActive = item.Item4 });

        TotalUsers = Users.Count;
        AdminCount = Users.Count(u => u.Role == "Admin");
        OperatorCount = Users.Count(u => u.Role == "Operator");
        ViewerCount = Users.Count(u => u.Role == "Viewer");
    }

    [RelayCommand]
    public void ManageRole(string role)
    {
        CurrentRole = role;
        CanModifySettings = role == "Admin";
        StatusMessage = "Switched to " + role + " role - settings " + (CanModifySettings ? "editable" : "read-only");
    }
}

public class UserInfo
{
    public string Username { get; set; } = "";
    public string Role { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsActive { get; set; }
}

// =================================================================
// 10. LOGS & AUDIT
// =================================================================
public partial class LogsAuditViewModel : ObservableObject
{
    private readonly TrafficSimulatorService _trafficService;
    private readonly SlaService _slaService;

    [ObservableProperty] private int totalLogEntries;
    [ObservableProperty] private string lastAction = "Application started";
    [ObservableProperty] private DateTime lastActionTime = DateTime.Now;
    [ObservableProperty] private string statusMessage = "Loading audit log...";
    [ObservableProperty] private ObservableCollection<string> logEntries = new();
    [ObservableProperty] private int metricCount;
    [ObservableProperty] private int slaCount;

    public LogsAuditViewModel(TrafficSimulatorService trafficService, SlaService slaService)
    {
        _trafficService = trafficService;
        _slaService = slaService;
        LoadLogsAsync();
    }

    private async void LoadLogsAsync()
    {
        try
        {
            MetricCount = await _trafficService.GetMetricCountAsync();
            var slaResults = await _slaService.GetAllResultsAsync();
            SlaCount = slaResults.Count();
            TotalLogEntries = MetricCount + SlaCount;

            LogEntries.Clear();
            LogEntries.Add("[" + DateTime.Now.ToString("HH:mm:ss") + "] SYSTEM: Audit log initialized");
            LogEntries.Add("[" + DateTime.Now.ToString("HH:mm:ss") + "] DB: " + MetricCount + " network metrics in database");
            LogEntries.Add("[" + DateTime.Now.ToString("HH:mm:ss") + "] DB: " + SlaCount + " SLA results in database");

            var recentMetrics = await _trafficService.GetRecentMetricsAsync(10);
            foreach (var m in recentMetrics)
            {
                LogEntries.Add("[" + m.Timestamp.ToString("HH:mm:ss") + "] METRIC: BW=" + m.Bandwidth.ToString("F1") + " Lat=" + m.Latency.ToString("F0") + "ms PL=" + m.PacketLoss.ToString("F2") + "%");
            }

            StatusMessage = "Audit log loaded - " + TotalLogEntries + " total entries";
        }
        catch (Exception ex) { StatusMessage = "Error: " + ex.Message; }
    }

    [RelayCommand]
    public async Task ExportLogs()
    {
        StatusMessage = "Exporting audit logs...";
        await Task.Delay(500);
        StatusMessage = "Audit log exported (simulated)";
    }

    [RelayCommand]
    public void RefreshLogs() => LoadLogsAsync();
}

// =================================================================
// 11. SETTINGS
// =================================================================
public partial class SettingsViewModel : ObservableObject
{
    private readonly TrafficSimulatorService _trafficService;
    private readonly SlaService _slaService;

    [ObservableProperty] private bool isDarkTheme = true;
    [ObservableProperty] private int refreshIntervalMs = 2000;
    [ObservableProperty] private string defaultSlaProfile = "Standard";
    [ObservableProperty] private string statusMessage = "Settings loaded";
    [ObservableProperty] private double guaranteedBandwidth;
    [ObservableProperty] private string dbPath = "sla_guardian.db";
    [ObservableProperty] private int totalMetrics;
    [ObservableProperty] private int totalSlaResults;

    public SettingsViewModel(TrafficSimulatorService trafficService, SlaService slaService)
    {
        _trafficService = trafficService;
        _slaService = slaService;
        GuaranteedBandwidth = _slaService.GuaranteedBandwidth;
        LoadSettingsDataAsync();
    }

    private async void LoadSettingsDataAsync()
    {
        try
        {
            TotalMetrics = await _trafficService.GetMetricCountAsync();
            var results = await _slaService.GetAllResultsAsync();
            TotalSlaResults = results.Count();
            StatusMessage = "Settings loaded - DB has " + TotalMetrics + " metrics, " + TotalSlaResults + " SLA records";
        }
        catch { }
    }

    [RelayCommand]
    public async Task SaveSettings()
    {
        _slaService.GuaranteedBandwidth = GuaranteedBandwidth;
        StatusMessage = "Settings saved - SLA threshold: " + GuaranteedBandwidth + " Mbps";
        await Task.CompletedTask;
    }

    [RelayCommand]
    public async Task ResetDatabase()
    {
        StatusMessage = "Resetting database...";
        await _trafficService.ClearAllMetricsAsync();
        TotalMetrics = 0;
        TotalSlaResults = 0;
        StatusMessage = "Database reset - all metrics cleared";
    }
}

// =================================================================
// 12. ROOT CAUSE ANALYZER
// =================================================================
public partial class RootCauseAnalyzerViewModel : ObservableObject
{
    private readonly TrafficSimulatorService _trafficService;
    private readonly SlaService _slaService;

    [ObservableProperty] private string detectedIssue = "Run analysis to detect issues";
    [ObservableProperty] private string recommendation = "";
    [ObservableProperty] private string statusMessage = "Ready for analysis";
    [ObservableProperty] private bool isAnalyzing;
    [ObservableProperty] private double avgBandwidth;
    [ObservableProperty] private double avgLatency;
    [ObservableProperty] private double avgPacketLoss;
    [ObservableProperty] private string severityLevel = "-";
    [ObservableProperty] private ObservableCollection<string> findings = new();

    public RootCauseAnalyzerViewModel(TrafficSimulatorService trafficService, SlaService slaService)
    {
        _trafficService = trafficService;
        _slaService = slaService;
    }

    [RelayCommand]
    public async Task AnalyzeIssue()
    {
        IsAnalyzing = true;
        Findings.Clear();
        StatusMessage = "Analyzing network patterns...";

        try
        {
            var metrics = await _trafficService.GetRecentMetricsAsync(50);
            if (metrics.Count < 3)
            {
                DetectedIssue = "Insufficient data";
                StatusMessage = "Need at least 3 data points. Start monitoring first.";
                IsAnalyzing = false;
                return;
            }

            await Task.Delay(800);

            AvgBandwidth = Math.Round(metrics.Average(m => m.Bandwidth), 2);
            AvgLatency = Math.Round(metrics.Average(m => m.Latency), 1);
            AvgPacketLoss = Math.Round(metrics.Average(m => m.PacketLoss), 3);

            int violations = metrics.Count(m => m.Bandwidth < _slaService.GuaranteedBandwidth);
            int highLatency = metrics.Count(m => m.Latency > 100);
            int highPktLoss = metrics.Count(m => m.PacketLoss > 2);

            if (violations > metrics.Count * 0.3)
            {
                DetectedIssue = "Sustained Bandwidth Degradation";
                SeverityLevel = "Critical";
                Recommendation = "Enable QoS optimization, investigate upstream link capacity, check for bandwidth-consuming applications.";
                Findings.Add(violations + "/" + metrics.Count + " samples below SLA threshold (" + _slaService.GuaranteedBandwidth + " Mbps)");
            }
            else if (highLatency > metrics.Count * 0.3)
            {
                DetectedIssue = "Network Congestion (High Latency)";
                SeverityLevel = "Warning";
                Recommendation = "Enable traffic shaping and QoS priority. Investigate routing paths and check for misconfiguration.";
                Findings.Add(highLatency + "/" + metrics.Count + " samples with latency > 100ms");
            }
            else if (highPktLoss > metrics.Count * 0.2)
            {
                DetectedIssue = "Packet Loss Issue";
                SeverityLevel = "Warning";
                Recommendation = "Check physical connections, switch port errors, and cable integrity.";
                Findings.Add(highPktLoss + "/" + metrics.Count + " samples with packet loss > 2%");
            }
            else
            {
                DetectedIssue = "No Critical Issues Detected";
                SeverityLevel = "Normal";
                Recommendation = "Network performance is within acceptable parameters. Continue monitoring.";
            }

            Findings.Add("Avg bandwidth: " + AvgBandwidth.ToString("F1") + " Mbps");
            Findings.Add("Avg latency: " + AvgLatency.ToString("F0") + " ms");
            Findings.Add("Avg packet loss: " + AvgPacketLoss.ToString("F2") + "%");
            Findings.Add("Data points analyzed: " + metrics.Count);

            StatusMessage = "Analysis complete - severity: " + SeverityLevel;
        }
        catch (Exception ex)
        {
            DetectedIssue = "Analysis failed";
            StatusMessage = "Error: " + ex.Message;
        }

        IsAnalyzing = false;
    }
}

// =================================================================
// 13. TRAFFIC ANALYZER
// =================================================================
public partial class TrafficAnalyzerViewModel : ObservableObject
{
    private readonly TrafficSimulatorService _trafficService;

    [ObservableProperty] private string topApp = "-";
    [ObservableProperty] private double topAppBandwidth;
    [ObservableProperty] private string statusMessage = "Loading traffic analysis...";
    [ObservableProperty] private ObservableCollection<AppTrafficInfo> appTraffic = new();
    [ObservableProperty] private double totalTrafficMbps;
    [ObservableProperty] private int activeFlows;

    public TrafficAnalyzerViewModel(TrafficSimulatorService trafficService)
    {
        _trafficService = trafficService;
        AnalyzeTrafficAsync();
    }

    private async void AnalyzeTrafficAsync()
    {
        var metrics = await _trafficService.GetRecentMetricsAsync(10);
        double totalBw = metrics.Count > 0 ? metrics.Average(m => m.Bandwidth) : 40;
        TotalTrafficMbps = Math.Round(totalBw, 2);
        var rand = new Random();

        var apps = new (string, double)[] {
            ("Microsoft Teams", 0.25), ("Web Browsing", 0.20), ("File Transfers", 0.15),
            ("Email (Exchange)", 0.10), ("Cloud Backup", 0.08), ("Windows Update", 0.07),
            ("Streaming Media", 0.06), ("VPN Tunnel", 0.05), ("Other", 0.04),
        };

        AppTraffic.Clear();
        foreach (var item in apps)
        {
            double bw = Math.Round(totalBw * item.Item2 * (0.8 + rand.NextDouble() * 0.4), 2);
            AppTraffic.Add(new AppTrafficInfo { AppName = item.Item1, BandwidthMbps = bw, SharePercent = Math.Round(bw / totalBw * 100, 1) });
        }

        TopApp = apps[0].Item1;
        TopAppBandwidth = AppTraffic.Count > 0 ? AppTraffic[0].BandwidthMbps : 0;
        ActiveFlows = 42 + rand.Next(0, 30);
        StatusMessage = "Traffic analysis complete - " + AppTraffic.Count + " applications tracked";
    }

    [RelayCommand]
    public void RefreshTopApps() => AnalyzeTrafficAsync();
}

public class AppTrafficInfo
{
    public string AppName { get; set; } = "";
    public double BandwidthMbps { get; set; }
    public double SharePercent { get; set; }
}

// =================================================================
// 14. CAPACITY PLANNING
// =================================================================
public partial class CapacityPlanningViewModel : ObservableObject
{
    private readonly TrafficSimulatorService _trafficService;
    private readonly PredictionService _predictionService;

    [ObservableProperty] private string recommendedUpgrade = "Calculating...";
    [ObservableProperty] private double projectedGrowth;
    [ObservableProperty] private string statusMessage = "Loading capacity data...";
    [ObservableProperty] private double currentCapacity;
    [ObservableProperty] private double peakUsage;
    [ObservableProperty] private double utilizationPercent;
    [ObservableProperty] private string timeToExhaustion = "-";
    [ObservableProperty] private double headroomMbps;

    public CapacityPlanningViewModel(TrafficSimulatorService trafficService, PredictionService predictionService)
    {
        _trafficService = trafficService;
        _predictionService = predictionService;
        CalculateCapacityAsync();
    }

    private async void CalculateCapacityAsync()
    {
        try
        {
            var metrics = await _trafficService.GetRecentMetricsAsync(100);
            if (metrics.Count < 3)
            {
                StatusMessage = "Need more data points for capacity planning.";
                return;
            }

            CurrentCapacity = 100;
            PeakUsage = Math.Round(metrics.Max(m => m.Bandwidth), 2);
            UtilizationPercent = Math.Round(PeakUsage / CurrentCapacity * 100, 1);
            HeadroomMbps = Math.Round(CurrentCapacity - PeakUsage, 2);

            var prediction = await _predictionService.PredictBandwidthAsync(50);
            if (prediction.HasValidPrediction)
            {
                ProjectedGrowth = Math.Round(prediction.Trend * 30, 2);
                double monthsToExhaust = HeadroomMbps > 0 && prediction.Trend > 0
                    ? Math.Round(HeadroomMbps / (prediction.Trend * 30), 0)
                    : 999;
                TimeToExhaustion = monthsToExhaust < 100 ? monthsToExhaust + " months" : "Not foreseeable";

                if (UtilizationPercent > 80)
                    RecommendedUpgrade = "Upgrade to " + ((int)(CurrentCapacity * 2)) + " Mbps within 1 month";
                else if (UtilizationPercent > 60)
                    RecommendedUpgrade = "Plan upgrade to " + ((int)(CurrentCapacity * 1.5)) + " Mbps within 3 months";
                else
                    RecommendedUpgrade = "Current capacity is sufficient";
            }

            StatusMessage = "Capacity analysis complete - " + UtilizationPercent.ToString("F0") + "% utilized";
        }
        catch (Exception ex) { StatusMessage = "Error: " + ex.Message; }
    }

    [RelayCommand]
    public void GenerateCapacityReport() => CalculateCapacityAsync();
}

// =================================================================
// 15. SMART NOTIFICATIONS
// =================================================================
public partial class SmartNotificationsViewModel : ObservableObject
{
    private readonly TrafficSimulatorService _trafficService;
    private readonly SlaService _slaService;

    [ObservableProperty] private bool isNotificationsEnabled = true;
    [ObservableProperty] private int notificationCount;
    [ObservableProperty] private string statusMessage = "Notification center ready";
    [ObservableProperty] private ObservableCollection<NotificationInfo> notifications = new();
    [ObservableProperty] private bool emailAlertsEnabled = true;
    [ObservableProperty] private bool smsAlertsEnabled;
    [ObservableProperty] private bool slackIntegrationEnabled;
    [ObservableProperty] private int criticalThreshold = 40;

    public SmartNotificationsViewModel(TrafficSimulatorService trafficService, SlaService slaService)
    {
        _trafficService = trafficService;
        _slaService = slaService;
        _trafficService.MetricGenerated += OnMetricForNotification;
        LoadNotificationHistoryAsync();
    }

    private async void LoadNotificationHistoryAsync()
    {
        try
        {
            var results = (await _slaService.GetAllResultsAsync()).ToList();
            var violations = results.Where(r => r.IsViolated).OrderByDescending(r => r.Timestamp).Take(10);
            foreach (var v in violations)
            {
                Notifications.Add(new NotificationInfo
                {
                    Title = "SLA Violation",
                    Message = "Bandwidth " + v.CurrentBandwidth.ToString("F1") + " Mbps < " + v.GuaranteedBandwidth + " Mbps",
                    Severity = "Critical",
                    Timestamp = v.Timestamp
                });
            }
            NotificationCount = Notifications.Count;
            StatusMessage = "Loaded " + NotificationCount + " historical notifications";
        }
        catch { }
    }

    private void OnMetricForNotification(object? sender, NetworkMetric metric)
    {
        if (!IsNotificationsEnabled) return;
        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            if (metric.Bandwidth < CriticalThreshold)
            {
                Notifications.Insert(0, new NotificationInfo
                {
                    Title = "Bandwidth Alert",
                    Message = "Bandwidth dropped to " + metric.Bandwidth.ToString("F1") + " Mbps (threshold: " + CriticalThreshold + " Mbps)",
                    Severity = "Critical",
                    Timestamp = DateTime.UtcNow
                });
                NotificationCount = Notifications.Count;
            }
            if (Notifications.Count > 100) Notifications.RemoveAt(Notifications.Count - 1);
        });
    }

    [RelayCommand]
    public void TestNotification()
    {
        Notifications.Insert(0, new NotificationInfo
        {
            Title = "Test Notification",
            Message = "This is a test notification from the Smart Notifications engine.",
            Severity = "Info",
            Timestamp = DateTime.UtcNow
        });
        NotificationCount = Notifications.Count;
        StatusMessage = "Test notification sent";
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
