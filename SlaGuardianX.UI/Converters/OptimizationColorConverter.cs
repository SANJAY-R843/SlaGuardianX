using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SlaGuardianX.UI
{
    /// <summary>
    /// Converter to change color based on optimization state
    /// </summary>
    public class OptimizationColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isOptimized)
            {
                return isOptimized ? (Brush)new SolidColorBrush(Color.FromRgb(124, 58, 237)) // Purple
                                  : (Brush)new SolidColorBrush(Color.FromRgb(136, 136, 136)); // Gray
            }
            return new SolidColorBrush(Color.FromRgb(136, 136, 136));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Heat map coloring for CPU usage (> 70% = Red, > 40% = Orange, else = Green)
    /// </summary>
    public class CpuHeatMapConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double cpu)
            {
                if (cpu > 70) return new SolidColorBrush(Color.FromRgb(239, 68, 68));   // Red
                if (cpu > 40) return new SolidColorBrush(Color.FromRgb(249, 115, 22));  // Orange
                return new SolidColorBrush(Color.FromRgb(34, 197, 94));                  // Green
            }
            return new SolidColorBrush(Color.FromRgb(156, 163, 175));                    // Gray default
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Heat map coloring for RAM usage
    /// </summary>
    public class RamHeatMapConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double ram)
            {
                if (ram > 500) return new SolidColorBrush(Color.FromRgb(239, 68, 68));   // Red (>500MB)
                if (ram > 200) return new SolidColorBrush(Color.FromRgb(249, 115, 22));  // Orange
                return new SolidColorBrush(Color.FromRgb(34, 197, 94));                   // Green
            }
            return new SolidColorBrush(Color.FromRgb(156, 163, 175));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Service status color (Running = Green, Stopped = Red, else = Yellow)
    /// </summary>
    public class ServiceStatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string status)
            {
                return status switch
                {
                    "Running" => new SolidColorBrush(Color.FromRgb(34, 197, 94)),      // Green
                    "Stopped" => new SolidColorBrush(Color.FromRgb(239, 68, 68)),      // Red
                    "Paused" => new SolidColorBrush(Color.FromRgb(249, 115, 22)),      // Orange
                    "StartPending" or "StopPending" => new SolidColorBrush(Color.FromRgb(234, 179, 8)), // Yellow
                    _ => new SolidColorBrush(Color.FromRgb(156, 163, 175))              // Gray
                };
            }
            return new SolidColorBrush(Color.FromRgb(156, 163, 175));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Boolean to Running/Stopped text
    /// </summary>
    public class BoolToStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isRunning)
                return isRunning ? "Running" : "Stopped";
            return "Unknown";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
