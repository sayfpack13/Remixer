using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Remixer.WPF;

public class MessageRoleToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string role)
        {
            return role switch
            {
                "user" => Application.Current.FindResource("AccentPrimary"), // Blue for user messages
                "assistant" => Application.Current.FindResource("BackgroundElevated"), // Elevated background for AI
                "system" => Application.Current.FindResource("BackgroundTertiary"), // Tertiary for system messages
                _ => Application.Current.FindResource("BackgroundSecondary")
            };
        }
        return Application.Current.FindResource("BackgroundSecondary");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class MessageRoleToAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string role)
        {
            return role switch
            {
                "user" => HorizontalAlignment.Right,
                "assistant" => HorizontalAlignment.Left,
                "system" => HorizontalAlignment.Center,
                _ => HorizontalAlignment.Left
            };
        }
        return HorizontalAlignment.Left;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class MessageRoleToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string role)
        {
            return role switch
            {
                "user" => "You",
                "assistant" => "AI Assistant",
                "system" => "System",
                _ => ""
            };
        }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class MessageRoleToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string role)
        {
            return role == "system" ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class InvertedBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class MessageRoleToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string role)
        {
            return role switch
            {
                "user" => Application.Current.FindResource("TextPrimary"), // White text on blue background
                "assistant" => Application.Current.FindResource("TextSecondary"), // Light gray on dark background
                "system" => Application.Current.FindResource("TextTertiary"),
                _ => Application.Current.FindResource("TextSecondary")
            };
        }
        return Application.Current.FindResource("TextSecondary");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class MessageRoleToTextForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string role)
        {
            return role switch
            {
                "user" => Application.Current.FindResource("TextPrimary"), // White text on blue background
                "assistant" => Application.Current.FindResource("TextPrimary"), // White text on dark background
                "system" => Application.Current.FindResource("TextSecondary"),
                _ => Application.Current.FindResource("TextPrimary")
            };
        }
        return Application.Current.FindResource("TextPrimary");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class MessageRoleToTimestampForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string role)
        {
            return role switch
            {
                "user" => Application.Current.FindResource("TextPrimary"), // White timestamp on blue background
                "assistant" => Application.Current.FindResource("TextTertiary"), // Gray timestamp on dark background
                "system" => Application.Current.FindResource("TextTertiary"),
                _ => Application.Current.FindResource("TextTertiary")
            };
        }
        return Application.Current.FindResource("TextTertiary");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

