using System;
using System.Globalization;
using System.Windows.Data;

namespace AeroAI.UI.Converters;

/// <summary>
/// Converts a level (0-1) and container width to pixel width.
/// </summary>
public class LevelToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return 0.0;

        if (values[0] is double level && values[1] is double containerWidth)
        {
            // Account for margin (4px total)
            var availableWidth = Math.Max(0, containerWidth - 4);
            return Math.Clamp(level, 0, 1) * availableWidth;
        }

        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

