namespace SlaGuardianX.Models
{
    using System;

    /// <summary>
    /// Represents the SLA compliance result and metrics for a given time period.
    /// Calculates SLA compliance percentage, violations, and risk assessment.
    /// </summary>
    public class SlaResult
    {
        public int Id { get; set; }

        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Guaranteed minimum bandwidth in Mbps
        /// </summary>
        public double GuaranteedBandwidth { get; set; }

        /// <summary>
        /// Current measured bandwidth in Mbps
        /// </summary>
        public double CurrentBandwidth { get; set; }

        /// <summary>
        /// Compliance percentage (0-100)
        /// </summary>
        public double CompliancePercentage { get; set; }

        /// <summary>
        /// Whether SLA is violated (bandwidth below guaranteed)
        /// </summary>
        public bool IsViolated { get; set; }

        /// <summary>
        /// Risk score from 0-100, where 100 is critical
        /// </summary>
        public double RiskScore { get; set; }

        /// <summary>
        /// Predicted future bandwidth trend (optional)
        /// </summary>
        public double? PredictedBandwidth { get; set; }

        /// <summary>
        /// Whether the system is in optimization mode
        /// </summary>
        public bool IsOptimized { get; set; }

        /// <summary>
        /// Optimized effective bandwidth after optimization (optional)
        /// </summary>
        public double? OptimizedBandwidth { get; set; }
    }
}
