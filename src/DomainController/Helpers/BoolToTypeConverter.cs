using System.Globalization;
using System.Windows.Data;

namespace TADDomainController.Helpers;

/// <summary>True → "Video", False → "JPEG".</summary>
public sealed class BoolToTypeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "Video" : "JPEG";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
