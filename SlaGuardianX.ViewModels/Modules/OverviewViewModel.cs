using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlaGuardianX.Services;
using SlaGuardianX.Models;
using System.Collections.ObjectModel;

namespace SlaGuardianX.ViewModels.Modules;

/// <summary>
/// Overview / Dashboard module ViewModel
/// Main NOC home screen with key metrics at a glance
/// </summary>
public partial class OverviewViewModel : ObservableObject
{
    private readonly SlaService _slaService;
    private readonly OptimizationService _optimizationService;
    private readonly PredictionService _predictionService;
    private readonly TrafficSimulatorService _trafficService;

    [ObservableProperty] private double currentBandwidth;
    [ObservableProperty] private double slaCompliancePercentage = 100;
    [ObservableProperty] private double riskScore;
    [ObservableProperty] private string optimizationStatus = "Inactive";
    [ObservableProperty] private int activeAlertCount;
    [ObservableProperty] private bool isMonitoring;
    [ObservableProperty] private string statusMessage = "System ready";
    [ObservableProperty] private double currentLatency;
    [ObservableProperty] private double currentPacketLoss;
    [ObservableProperty] private double currentUptime = 99.9;
    [ObservableProperty] private int totalDataPoints;
    [ObservableProperty] private string lastUpdated = "—";
    [ObservableProperty] private double predictedBandwidth;

    public OverviewViewModel(SlaService slaService, OptimizationService optimizationService,
        PredictionService predictionService, TrafficSimulatorService trafficService)
    {
        _slaService = slaService;
        _optimizationService = optimizationService;
        _predictionService = predictionService;
        _trafficService = trafficService;
        _trafficService.MetricGenerated += OnMetricGenerated;
        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        await RefreshMetricsAsync();
    }

    private void OnMetricGenerated(object? sender, NetworkMetric metric)
    {
        System.Windows.Application.Current?.Dispatcher?.Invoke(async () =>
        {
            CurrentBandwidth = Math.Round(metric.Bandwidth, 2);
            CurrentLatency = Math.Round(metric.Latency, 1);
            CurrentPacketLoss = Math.Round(metric.PacketLoss, 3);
            CurrentUptime = Math.Round(metric.Uptime, 2);
            LastUpdated = DateTime.Now.ToString("HH:mm:ss");
            TotalDataPoints++;

            // Calculate SLA on each new metric
            try
            {
                var slaResult = await _slaService.CalculateSlaAsync(metric);
                SlaCompliancePercentage = Math.Round(slaResult.CompliancePercentage, 1);
                RiskScore = Math.Round(slaResult.RiskScore, 1);
                if (slaResult.IsViolated) ActiveAlertCount++;
            }
            catch { }
        });
    }

    [RelayCommand]
    public void StartMonitoring()
    {
        _trafficService.Start();
        IsMonitoring = true;
        StatusMessage = "Live monitoring active";
    }

    [RelayCommand]
    public void StopMonitoring()
    {
        _trafficService.Stop();
        IsMonitoring = false;
        StatusMessage = "Monitoring paused";
    }

    [RelayCommand]
    public async Task RefreshMetrics()
    {
        await RefreshMetricsAsync();
    }

    private async Task RefreshMetricsAsync()
    {
        try
        {
            var metrics = await _slaService.GetRecentResultsAsync(1);
            if (metrics.Any())
            {
                var latest = metrics.First();
                CurrentBandwidth = Math.Round(latest.CurrentBandwidth, 2);
                SlaCompliancePercentage = Math.Round(latest.CompliancePercentage, 1);
                RiskScore = Math.Round(latest.RiskScore, 1);
                OptimizationStatus = latest.IsOptimized ? "Active" : "Inactive";
            }

            TotalDataPoints = await _trafficService.GetMetricCountAsync();
            OptimizationStatus = _optimizationService.IsOptimizationEnabled ? "Active" : "Inactive";
            IsMonitoring = false; // will be true if timer is running

            var prediction = await _predictionService.PredictBandwidthAsync(50);
            if (prediction.HasValidPrediction)
            {
                PredictedBandwidth = Math.Round(prediction.PredictedBandwidth, 2);
            }

            StatusMessage = "Dashboard refreshed";
        }
        catch (Exception ex)
        {
            StatusMessage = "Error: " + ex.Message;
        }
    }
}
