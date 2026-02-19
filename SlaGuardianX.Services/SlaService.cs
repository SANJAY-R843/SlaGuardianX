namespace SlaGuardianX.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using SlaGuardianX.Data;
    using SlaGuardianX.Models;

    /// <summary>
    /// Core SLA compliance engine.
    /// Calculates SLA compliance percentage, violations, and risk scores.
    /// </summary>
    public class SlaService
    {
        private readonly IRepository<SlaResult> _slaRepository;
        private readonly IRepository<NetworkMetric> _metricRepository;

        public double GuaranteedBandwidth { get; set; } = 40.0; // Mbps default

        public SlaService(
            IRepository<SlaResult> slaRepository,
            IRepository<NetworkMetric> metricRepository)
        {
            _slaRepository = slaRepository ?? throw new ArgumentNullException(nameof(slaRepository));
            _metricRepository = metricRepository ?? throw new ArgumentNullException(nameof(metricRepository));
        }

        /// <summary>
        /// Calculate SLA compliance for the current metric.
        /// </summary>
        public async Task<SlaResult> CalculateSlaAsync(NetworkMetric metric, double? predictedBandwidth = null)
        {
            if (metric == null)
                throw new ArgumentNullException(nameof(metric));

            bool isViolated = metric.Bandwidth < GuaranteedBandwidth;
            double compliance = (metric.Bandwidth / GuaranteedBandwidth) * 100;
            compliance = Math.Min(100, Math.Max(0, compliance)); // Clamp between 0-100

            // Calculate risk score based on multiple factors
            double riskScore = CalculateRiskScore(metric, predictedBandwidth);

            var result = new SlaResult
            {
                Timestamp = DateTime.UtcNow,
                GuaranteedBandwidth = GuaranteedBandwidth,
                CurrentBandwidth = metric.Bandwidth,
                CompliancePercentage = compliance,
                IsViolated = isViolated,
                RiskScore = riskScore,
                PredictedBandwidth = predictedBandwidth,
                IsOptimized = false
            };

            await _slaRepository.AddAsync(result);
            return result;
        }

        /// <summary>
        /// Calculate risk score based on:
        /// - Current bandwidth vs guaranteed
        /// - Latency
        /// - Packet loss
        /// - Prediction trend
        /// </summary>
        private double CalculateRiskScore(NetworkMetric metric, double? predictedBandwidth)
        {
            double riskScore = 0;

            // Bandwidth risk (40% weight)
            double bandwidthRatio = metric.Bandwidth / GuaranteedBandwidth;
            double bandwidthRisk = Math.Max(0, (1 - bandwidthRatio) * 100);
            riskScore += bandwidthRisk * 0.4;

            // Latency risk (20% weight, threshold 100ms is "bad")
            double latencyRisk = Math.Min(100, (metric.Latency / 100) * 100);
            riskScore += latencyRisk * 0.2;

            // Packet loss risk (20% weight, threshold 1% is "bad")
            double packetLossRisk = metric.PacketLoss > 1 ? (metric.PacketLoss / 1) * 100 : 0;
            riskScore += Math.Min(100, packetLossRisk) * 0.2;

            // Prediction risk (20% weight)
            if (predictedBandwidth.HasValue)
            {
                double predictedRatio = predictedBandwidth.Value / GuaranteedBandwidth;
                double predictionRisk = Math.Max(0, (1 - predictedRatio) * 100);
                riskScore += predictionRisk * 0.2;
            }

            return Math.Min(100, Math.Max(0, riskScore));
        }

        /// <summary>
        /// Get overall SLA compliance for a time period (last N metrics)
        /// </summary>
        public async Task<double> GetCompliancePercentageAsync(int timePeriodMinutes = 60)
        {
            var cutoffTime = DateTime.UtcNow.AddMinutes(-timePeriodMinutes);
            var results = await _slaRepository.FindAsync(r => r.Timestamp >= cutoffTime);

            if (!results.Any())
                return 100;

            int violationCount = results.Count(r => r.IsViolated);
            return ((results.Count() - violationCount) / (double)results.Count()) * 100;
        }

        /// <summary>
        /// Get all SLA results
        /// </summary>
        public async Task<IEnumerable<SlaResult>> GetAllResultsAsync()
        {
            return await _slaRepository.GetAllAsync();
        }

        /// <summary>
        /// Get recent SLA results
        /// </summary>
        public async Task<List<SlaResult>> GetRecentResultsAsync(int count = 50)
        {
            var all = await _slaRepository.GetAllAsync();
            return all.OrderByDescending(r => r.Timestamp).Take(count).OrderBy(r => r.Timestamp).ToList();
        }
    }
}
