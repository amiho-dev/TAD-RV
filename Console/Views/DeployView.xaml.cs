using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace TadConsole.Views;

public partial class DeployView : UserControl
{
    public DeployView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Converts (progress %, container width) → pixel width for the progress bar.
    /// </summary>
    public static readonly IMultiValueConverter ProgressWidthConverter = new ProgressToWidthConverter();

    private sealed class ProgressToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2
                && values[0] is int progress
                && values[1] is double containerWidth)
            {
                return containerWidth * (progress / 100.0);
            }
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
