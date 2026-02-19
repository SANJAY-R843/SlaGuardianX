namespace SlaGuardianX.AI
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// AI/ML module for bandwidth prediction.
    /// Uses simple linear regression for trend prediction.
    /// </summary>
    public class BandwidthPredictor
    {
        /// <summary>
        /// Predict the next bandwidth value using linear regression.
        /// </summary>
        public double PredictNext(List<double> historicalBandwidth)
        {
            if (historicalBandwidth == null || historicalBandwidth.Count == 0)
                throw new ArgumentException("Historical bandwidth data required");

            if (historicalBandwidth.Count == 1)
                return historicalBandwidth[0];

            // Linear regression: y = mx + b
            // where x is time (index) and y is bandwidth
            double m = CalculateSlope(historicalBandwidth);
            double b = CalculateIntercept(historicalBandwidth, m);

            // Predict at the next time point
            int nextIndex = historicalBandwidth.Count;
            double predicted = (m * nextIndex) + b;

            // Apply smoothing to avoid extreme outliers
            double average = historicalBandwidth.Average();
            double stdDev = CalculateStdDev(historicalBandwidth, average);

            // Clamp prediction to reasonable range
            double lowerBound = Math.Max(15, average - (2 * stdDev));
            double upperBound = average + (2 * stdDev);

            predicted = Math.Max(lowerBound, Math.Min(upperBound, predicted));

            return predicted;
        }

        /// <summary>
        /// Calculate slope using least squares regression
        /// </summary>
        private double CalculateSlope(List<double> values)
        {
            int n = values.Count;
            double sumXY = 0;
            double sumX = 0;
            double sumY = 0;
            double sumX2 = 0;

            for (int i = 0; i < n; i++)
            {
                sumXY += i * values[i];
                sumX += i;
                sumY += values[i];
                sumX2 += i * i;
            }

            double numerator = (n * sumXY) - (sumX * sumY);
            double denominator = (n * sumX2) - (sumX * sumX);

            if (denominator == 0)
                return 0;

            return numerator / denominator;
        }

        /// <summary>
        /// Calculate intercept using least squares regression
        /// </summary>
        private double CalculateIntercept(List<double> values, double slope)
        {
            int n = values.Count;
            double sumX = 0;
            double sumY = 0;

            for (int i = 0; i < n; i++)
            {
                sumX += i;
                sumY += values[i];
            }

            double avgX = sumX / n;
            double avgY = sumY / n;

            return avgY - (slope * avgX);
        }

        /// <summary>
        /// Calculate standard deviation
        /// </summary>
        private double CalculateStdDev(List<double> values, double average)
        {
            int n = values.Count;
            double sumSquaredDiff = 0;

            foreach (var value in values)
            {
                double diff = value - average;
                sumSquaredDiff += diff * diff;
            }

            return Math.Sqrt(sumSquaredDiff / n);
        }
    }
}
