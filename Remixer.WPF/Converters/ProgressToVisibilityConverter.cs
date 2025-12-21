using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Remixer.WPF;

public class ProgressToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double progress)
        {
            return progress > 0 && progress < 100 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

