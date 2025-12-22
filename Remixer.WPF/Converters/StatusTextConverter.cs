using System;
using System.Globalization;
using System.Windows.Data;

namespace Remixer.WPF;

public class StatusTextConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 4) return "Ready";

        bool isAudioLoaded = values[0] is bool loaded && loaded;
        bool isProcessing = values[1] is bool processing && processing;
        bool isLoadingAudio = values[2] is bool loading && loading;
        bool isApplyingEffects = values[3] is bool applying && applying;

        if (isLoadingAudio)
            return "Loading audio...";
        else if (isApplyingEffects)
            return "Applying effects...";
        else if (isProcessing)
            return "Processing...";
        else if (isAudioLoaded)
            return "Ready to remix!";
        else
            return "Load audio to get started";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
