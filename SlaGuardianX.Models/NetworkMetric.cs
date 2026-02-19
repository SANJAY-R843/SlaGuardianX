namespace SlaGuardianX.Models
{
    using System;

    /// <summary>
    /// Represents a single network metric measurement.
    /// Stores bandwidth, latency, packet loss, and other network performance indicators.
    /// </summary>
    public class NetworkMetric
    {
        public int Id { get; set; }

        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Bandwidth in Mbps
        /// </summary>
        public double Bandwidth { get; set; }

        /// <summary>
        /// Latency in milliseconds
        /// </summary>
        public double Latency { get; set; }

        /// <summary>
        /// Packet loss as percentage (0-100)
        /// </summary>
        public double PacketLoss { get; set; }

        /// <summary>
        /// Uptime percentage (0-100)
        /// </summary>
        public double Uptime { get; set; }
    }
}
