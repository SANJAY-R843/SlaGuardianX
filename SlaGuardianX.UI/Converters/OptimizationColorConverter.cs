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
}
