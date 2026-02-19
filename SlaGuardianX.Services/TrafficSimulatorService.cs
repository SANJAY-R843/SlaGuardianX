namespace SlaGuardianX.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using SlaGuardianX.Data;
    using SlaGuardianX.Models;

    /// <summary>
    /// Simulates network traffic and generates realistic network metrics.
    /// Used for demo and testing purposes.
    /// </summary>
    public class TrafficSimulatorService
    {
        private readonly IRepository<NetworkMetric> _repository;
        private Random _random;
        private Timer _simulationTimer;
        private double _baselineBandwidth = 40.0; // Mbps
        private double _bandwidthTrend = 0.0; // Trend direction

        public event EventHandler<NetworkMetric> MetricGenerated;

        public TrafficSimulatorService(IRepository<NetworkMetric> repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _random = new Random();
        }

        /// <summary>
        /// Start simulating network traffic.
        /// Generates a new metric every 2 seconds.
        /// </summary>
        public void Start()
        {
            if (_simulationTimer == null)
            {
                _simulationTimer = new Timer(GenerateMetric, null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
            }
        }

        /// <summary>
        /// Stop the traffic simulator.
        /// </summary>
        public void Stop()
        {
            _simulationTimer?.Dispose();
            _simulationTimer = null;
        }

        /// <summary>
        /// Generate a single network metric with realistic fluctuations.
        /// </summary>
        private async void GenerateMetric(object state)
        {
            try
            {
                // Simulate bandwidth with trend and random noise
                double variation = (_random.NextDouble() - 0.5) * 15; // ±7.5 Mbps variation
                double trend = _bandwidthTrend * (_random.NextDouble() - 0.5) * 2; // Trend component
                double bandwidth = Math.Max(20, _baselineBandwidth + variation + trend);

                // Update trend randomly (tendency to go up or down slightly)
                _bandwidthTrend += (_random.NextDouble() - 0.5) * 0.1;
                _bandwidthTrend = Math.Max(-1, Math.Min(1, _bandwidthTrend)); // Clamp trend

                // Simulate latency (20-150ms)
                double latency = 20 + (_random.NextDouble() * 130);

                // Simulate packet loss (0-5%)
                double packetLoss = _random.NextDouble() * 5;

                // Simulate uptime (95-100%)
                double uptime = 95 + (_random.NextDouble() * 5);

                var metric = new NetworkMetric
                {
                    Timestamp = DateTime.UtcNow,
                    Bandwidth = bandwidth,
                    Latency = latency,
                    PacketLoss = packetLoss,
                    Uptime = uptime
                };

                // Save to database
                await _repository.AddAsync(metric);

                // Raise event for subscribers (ViewModels)
                MetricGenerated?.Invoke(this, metric);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Traffic simulation error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the last N network metrics from the database.
        /// </summary>
        public async Task<List<NetworkMetric>> GetRecentMetricsAsync(int count = 50)
        {
            var allMetrics = await _repository.GetAllAsync();
            return allMetrics.OrderByDescending(m => m.Timestamp).Take(count).OrderBy(m => m.Timestamp).ToList();
        }

        /// <summary>
        /// Get metric count
        /// </summary>
        public async Task<int> GetMetricCountAsync()
        {
            return await _repository.CountAsync();
        }

        /// <summary>
        /// Clear all metrics (for testing/reset)
        /// </summary>
        public async Task ClearAllMetricsAsync()
        {
            var all = await _repository.GetAllAsync();
            foreach (var metric in all)
            {
                await _repository.DeleteAsync(metric);
            }
        }
    }
}
