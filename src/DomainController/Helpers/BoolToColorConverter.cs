using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TADDomainController.Helpers;

/// <summary>True → highlight colour, False → secondary text colour.</summary>
public sealed class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true
            ? new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50))   // SuccessBrush green
            : new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E));  // TextSecondaryBrush

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
