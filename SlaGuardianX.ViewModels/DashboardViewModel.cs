namespace SlaGuardianX.ViewModels
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Windows.Input;
    using CommunityToolkit.Mvvm.ComponentModel;
    using CommunityToolkit.Mvvm.Input;
    using SlaGuardianX.Data;
    using SlaGuardianX.Models;
    using SlaGuardianX.Services;

    /// <summary>
    /// ViewModel for the Dashboard view.
    /// Manages real-time data binding, commands, and application state.
    /// </summary>
    public partial class DashboardViewModel : ObservableObject
    {
        private readonly TrafficSimulatorService _simulatorService;
        private readonly SlaService _slaService;
        private readonly OptimizationService _optimizationService;
        private readonly PredictionService _predictionService;
        private readonly IRepository<NetworkMetric> _metricRepository;
        private readonly IRepository<SlaResult> _slaRepository;

        [ObservableProperty]
        private double currentBandwidth;

        [ObservableProperty]
        private double guaranteedBandwidth = 40.0;

        [ObservableProperty]
        private double slaCompliancePercentage;

        [ObservableProperty]
        private double riskScore;

        [ObservableProperty]
        private string riskLevel;

        [ObservableProperty]
        private double predictedBandwidth;

        [ObservableProperty]
        private double optimizedBandwidth;

        [ObservableProperty]
        private bool isOptimizationEnabled;

        [ObservableProperty]
        private bool isMonitoring;

        [ObservableProperty]
        private ObservableCollection<NetworkMetricDataPoint> bandwidthChartData
            = new ObservableCollection<NetworkMetricDataPoint>();

        [ObservableProperty]
        private ObservableCollection<NetworkMetricDataPoint> predictionChartData
            = new ObservableCollection<NetworkMetricDataPoint>();

        [ObservableProperty]
        private string currentStatus = "Initializing...";

        [ObservableProperty]
        private int totalMetricsCount;

        [ObservableProperty]
        private double averageBandwidth;

        public DashboardViewModel(
            TrafficSimulatorService simulatorService,
            SlaService slaService,
            OptimizationService optimizationService,
            PredictionService predictionService,
            IRepository<NetworkMetric> metricRepository,
            IRepository<SlaResult> slaRepository)
        {
            _simulatorService = simulatorService ?? throw new ArgumentNullException(nameof(simulatorService));
            _slaService = slaService ?? throw new ArgumentNullException(nameof(slaService));
            _optimizationService = optimizationService ?? throw new ArgumentNullException(nameof(optimizationService));
            _predictionService = predictionService ?? throw new ArgumentNullException(nameof(predictionService));
            _metricRepository = metricRepository ?? throw new ArgumentNullException(nameof(metricRepository));
            _slaRepository = slaRepository ?? throw new ArgumentNullException(nameof(slaRepository));

            GuaranteedBandwidth = _slaService.GuaranteedBandwidth;

            // Subscribe to metric generated events
            _simulatorService.MetricGenerated += OnMetricGenerated;
        }

        /// <summary>
        /// Start monitoring network and updating dashboard
        /// </summary>
        public async Task StartMonitoringAsync()
        {
            IsMonitoring = true;
            CurrentStatus = "Monitoring active...";
            _simulatorService.Start();
            await Task.CompletedTask;
        }

        /// <summary>
        /// Stop monitoring
        /// </summary>
        public async Task StopMonitoringAsync()
        {
            IsMonitoring = false;
            CurrentStatus = "Monitoring stopped";
            _simulatorService.Stop();
            await Task.CompletedTask;
        }

        /// <summary>
        /// Handle when a new metric is generated
        /// </summary>
        private async void OnMetricGenerated(object sender, NetworkMetric metric)
        {
            try
            {
                CurrentBandwidth = metric.Bandwidth;

                // Calculate SLA
                var predictionTask = _predictionService.PredictBandwidthAsync();
                var predictionResult = await predictionTask;
                PredictedBandwidth = predictionResult.HasValidPrediction
                    ? predictionResult.PredictedBandwidth
                    : metric.Bandwidth;

                var slaResult = await _slaService.CalculateSlaAsync(metric, PredictedBandwidth);

                SlaCompliancePercentage = slaResult.CompliancePercentage;
                RiskScore = slaResult.RiskScore;
                RiskLevel = GetRiskLevel(RiskScore);

                // Update chart data
                BandwidthChartData.Add(new NetworkMetricDataPoint
                {
                    Timestamp = metric.Timestamp,
                    Value = metric.Bandwidth
                });

                PredictionChartData.Add(new NetworkMetricDataPoint
                {
                    Timestamp = metric.Timestamp,
                    Value = PredictedBandwidth
                });

                // Keep only last 50 points for performance
                if (BandwidthChartData.Count > 50)
                    BandwidthChartData.RemoveAt(0);
                if (PredictionChartData.Count > 50)
                    PredictionChartData.RemoveAt(0);

                // Update stats
                TotalMetricsCount = await _metricRepository.CountAsync();
                var allMetrics = await _metricRepository.GetAllAsync();
                if (allMetrics.Any())
                    AverageBandwidth = allMetrics.Average(m => m.Bandwidth);
            }
            catch (Exception ex)
            {
                CurrentStatus = $"Error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Dashboard error: {ex}");
            }
        }

        /// <summary>
        /// Enable optimization
        /// </summary>
        [RelayCommand]
        public async Task EnableOptimization()
        {
            try
            {
                if (!IsOptimizationEnabled)
                {
                    double optimized = await _optimizationService.EnableOptimizationAsync(CurrentBandwidth);
                    OptimizedBandwidth = optimized;
                    IsOptimizationEnabled = true;
                    CurrentStatus = "Optimization ENABLED - Bandwidth increased!";

                    // Update the latest SLA result with optimization
                    var recentResults = await _slaService.GetRecentResultsAsync(1);
                    if (recentResults.Any())
                    {
                        await _optimizationService.UpdateSlaResultWithOptimizationAsync(recentResults.First(), optimized);
                    }
                }
            }
            catch (Exception ex)
            {
                CurrentStatus = $"Optimization error: {ex.Message}";
            }
        }

        /// <summary>
        /// Disable optimization
        /// </summary>
        [RelayCommand]
        public async Task DisableOptimization()
        {
            try
            {
                if (IsOptimizationEnabled)
                {
                    await _optimizationService.DisableOptimizationAsync();
                    IsOptimizationEnabled = false;
                    OptimizedBandwidth = 0;
                    CurrentStatus = "Optimization disabled";
                }
            }
            catch (Exception ex)
            {
                CurrentStatus = $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Clear all collected data
        /// </summary>
        [RelayCommand]
        public async Task ClearData()
        {
            try
            {
                await _simulatorService.ClearAllMetricsAsync();
                BandwidthChartData.Clear();
                PredictionChartData.Clear();
                TotalMetricsCount = 0;
                AverageBandwidth = 0;
                CurrentStatus = "All data cleared";
            }
            catch (Exception ex)
            {
                CurrentStatus = $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Get risk level description from score
        /// </summary>
        private string GetRiskLevel(double score)
        {
            if (score < 25)
                return "🟢 SAFE";
            else if (score < 50)
                return "🟡 WARNING";
            else if (score < 75)
                return "🟠 HIGH";
            else
                return "🔴 CRITICAL";
        }
    }

    /// <summary>
    /// Data point for chart visualization
    /// </summary>
    public class NetworkMetricDataPoint
    {
        public DateTime Timestamp { get; set; }
        public double Value { get; set; }
    }
}
