namespace SlaGuardianX.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using SlaGuardianX.AI;
    using SlaGuardianX.Data;
    using SlaGuardianX.Models;

    /// <summary>
    /// AI-based bandwidth prediction service.
    /// Predicts future bandwidth trends using historical data.
    /// </summary>
    public class PredictionService
    {
        private readonly IRepository<NetworkMetric> _repository;
        private readonly BandwidthPredictor _predictor;

        public PredictionService(IRepository<NetworkMetric> repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _predictor = new BandwidthPredictor();
        }

        /// <summary>
        /// Predict future bandwidth based on the last N metrics.
        /// </summary>
        public async Task<PredictionResult> PredictBandwidthAsync(int lookbackPeriod = 50)
        {
            try
            {
                // Get recent metrics
                var allMetrics = await _repository.GetAllAsync();
                var recentMetrics = allMetrics
                    .OrderByDescending(m => m.Timestamp)
                    .Take(lookbackPeriod)
                    .OrderBy(m => m.Timestamp)
                    .ToList();

                if (recentMetrics.Count < 3)
                {
                    // Not enough data
                    return new PredictionResult
                    {
                        HasValidPrediction = false,
                        Message = "Insufficient data for prediction"
                    };
                }

                // Extract bandwidth values
                var bandwidthValues = recentMetrics.Select(m => m.Bandwidth).ToList();

                // Use the predictor
                double predictedBandwidth = _predictor.PredictNext(bandwidthValues);
                double trend = CalculateTrend(bandwidthValues);

                return new PredictionResult
                {
                    HasValidPrediction = true,
                    PredictedBandwidth = predictedBandwidth,
                    CurrentBandwidth = bandwidthValues.Last(),
                    Trend = trend,
                    DataPointsUsed = bandwidthValues.Count,
                    Message = "Prediction successful"
                };
            }
            catch (Exception ex)
            {
                return new PredictionResult
                {
                    HasValidPrediction = false,
                    Message = $"Prediction error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Calculate bandwidth trend (positive = increasing, negative = decreasing)
        /// </summary>
        private double CalculateTrend(List<double> values)
        {
            if (values.Count < 2)
                return 0;

            // Simple linear trend: (last - first) / count
            double trend = (values.Last() - values.First()) / values.Count;
            return trend;
        }
    }

    /// <summary>
    /// Result of bandwidth prediction
    /// </summary>
    public class PredictionResult
    {
        public bool HasValidPrediction { get; set; }
        public double PredictedBandwidth { get; set; }
        public double CurrentBandwidth { get; set; }
        public double Trend { get; set; }
        public int DataPointsUsed { get; set; }
        public string Message { get; set; }
    }
}
