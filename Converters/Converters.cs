using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace llama_server_winui.Converters
{
    /// <summary>
    /// Converts null to Collapsed, non-null to Visible
    /// Used for showing UI elements only when data is available
    /// </summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value != null ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts null to Visible, non-null to Collapsed
    /// Used for showing empty state messages when no data is available
    /// </summary>
    public class InverseNullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value == null ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts boolean to Visibility (true = Visible, false = Collapsed)
    /// Standard converter for conditional UI visibility
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is Visibility visibility)
            {
                return visibility == Visibility.Visible;
            }
            return false;
        }
    }

    /// <summary>
    /// Converts boolean to inverse Visibility (true = Collapsed, false = Visible)
    /// Used for showing alternative UI when a condition is false
    /// </summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is Visibility visibility)
            {
                return visibility == Visibility.Collapsed;
            }
            return true;
        }
    }

    /// <summary>
    /// Converts TimeSpan to readable string format (HH:mm:ss)
    /// Used for displaying server uptime
    /// </summary>
    public class TimeSpanToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is TimeSpan timeSpan)
            {
                if (timeSpan.TotalDays >= 1)
                {
                    return $"{(int)timeSpan.TotalDays}d {timeSpan.Hours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
                }
                return $"{timeSpan.Hours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
            }
            return "--:--:--";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts double (percentage) to formatted string with % symbol
    /// Used for CPU usage display
    /// </summary>
    public class PercentageToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is double percentage)
            {
                return $"{percentage:F1}%";
            }
            return "--";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts long (bytes) to readable memory format (MB or GB)
    /// Used for memory usage display
    /// </summary>
    public class BytesToMemoryStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is long bytes)
            {
                double mb = bytes / 1048576.0;
                if (mb >= 1024)
                {
                    return $"{(mb / 1024):F2} GB";
                }
                return $"{mb:F1} MB";
            }
            return "--";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts ProcessState enum to user-friendly string
    /// Used for displaying server status
    /// </summary>
    public class ProcessStateToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is Services.ProcessState state)
            {
                return state switch
                {
                    Services.ProcessState.Stopped => "Stopped",
                    Services.ProcessState.Starting => "Starting...",
                    Services.ProcessState.Running => "Running",
                    Services.ProcessState.Stopping => "Stopping...",
                    Services.ProcessState.Error => "Error",
                    _ => "Unknown"
                };
            }
            return "Unknown";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts ProcessState to status color brush
    /// Used for visual state indicators
    /// </summary>
    public class ProcessStateToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is Services.ProcessState state)
            {
                return state switch
                {
                    Services.ProcessState.Running => new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(255, 76, 175, 80)), // Green
                    Services.ProcessState.Starting => new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(255, 255, 152, 0)), // Orange
                    Services.ProcessState.Stopping => new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(255, 255, 152, 0)), // Orange
                    Services.ProcessState.Error => new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(255, 244, 67, 54)), // Red
                    _ => new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(255, 158, 158, 158)) // Gray
                };
            }
            return new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 158, 158, 158));
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Special converter for Run button visibility
    /// Shows when installed AND not running
    /// </summary>
    public class RunButtonVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            // Value is the DataContext (LlamaEngine)
            if (value is bool isInstalled)
            {
                // This is called with IsInstalled binding
                // We need access to both IsInstalled and IsServerRunning
                // Since we can't do that in converter, we'll handle it differently
                return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
