namespace SlaGuardianX.Services
{
    using System;
    using System.Threading.Tasks;
    using SlaGuardianX.Data;
    using SlaGuardianX.Models;

    /// <summary>
    /// Adaptive Bandwidth Optimization Engine.
    /// Simulates intelligent bandwidth allocation and traffic prioritization.
    /// </summary>
    public class OptimizationService
    {
        private readonly IRepository<SlaResult> _slaRepository;
        private bool _isOptimizationEnabled = false;

        public bool IsOptimizationEnabled => _isOptimizationEnabled;

        /// <summary>
        /// Optimization boost factor (35% increase by default)
        /// </summary>
        public const double OptimizationBoostFactor = 0.35;

        public OptimizationService(IRepository<SlaResult> slaRepository)
        {
            _slaRepository = slaRepository ?? throw new ArgumentNullException(nameof(slaRepository));
        }

        /// <summary>
        /// Enable optimization and calculate optimized bandwidth.
        /// Simulates:
        /// - QoS prioritization
        /// - Low-priority traffic suppression
        /// - Critical traffic boosting
        /// </summary>
        public async Task<double> EnableOptimizationAsync(double currentBandwidth)
        {
            _isOptimizationEnabled = true;

            // Calculate optimized bandwidth
            // Physical bandwidth: currentBandwidth
            // Effective bandwidth after optimization: currentBandwidth * (1 + OptimizationBoostFactor)
            double optimizedBandwidth = currentBandwidth * (1 + OptimizationBoostFactor);

            return await Task.FromResult(optimizedBandwidth);
        }

        /// <summary>
        /// Disable optimization
        /// </summary>
        public async Task DisableOptimizationAsync()
        {
            _isOptimizationEnabled = false;
            return;
        }

        /// <summary>
        /// Update the latest SLA result with optimization details
        /// </summary>
        public async Task UpdateSlaResultWithOptimizationAsync(SlaResult result, double optimizedBandwidth)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            result.IsOptimized = true;
            result.OptimizedBandwidth = optimizedBandwidth;

            // Recalculate compliance based on optimized bandwidth
            result.CompliancePercentage = (optimizedBandwidth / result.GuaranteedBandwidth) * 100;
            result.CompliancePercentage = Math.Min(100, Math.Max(0, result.CompliancePercentage));

            // If optimized bandwidth meets SLA, reduce risk
            if (optimizedBandwidth >= result.GuaranteedBandwidth)
            {
                result.IsViolated = false;
                result.RiskScore = Math.Max(0, result.RiskScore - 25); // Reduce risk by 25%
            }

            await _slaRepository.UpdateAsync(result);
        }

        /// <summary>
        /// Get optimization details
        /// </summary>
        public OptimizationInfo GetOptimizationInfo(double currentBandwidth)
        {
            if (!_isOptimizationEnabled)
                return new OptimizationInfo { IsEnabled = false };

            double optimizedBandwidth = currentBandwidth * (1 + OptimizationBoostFactor);
            double improvementMbps = optimizedBandwidth - currentBandwidth;
            double improvementPercent = (improvementMbps / currentBandwidth) * 100;

            return new OptimizationInfo
            {
                IsEnabled = true,
                OriginalBandwidth = currentBandwidth,
                OptimizedBandwidth = optimizedBandwidth,
                ImprovementMbps = improvementMbps,
                ImprovementPercent = improvementPercent,
                BoostFactor = OptimizationBoostFactor
            };
        }
    }

    /// <summary>
    /// Information about the current optimization state
    /// </summary>
    public class OptimizationInfo
    {
        public bool IsEnabled { get; set; }
        public double OriginalBandwidth { get; set; }
        public double OptimizedBandwidth { get; set; }
        public double ImprovementMbps { get; set; }
        public double ImprovementPercent { get; set; }
        public double BoostFactor { get; set; }
    }
}
