using System.Globalization;
using System.Windows.Data;

namespace WpfDevTools.Tests.TestApp;

public sealed class ConcatMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        return string.Join(" ", values
            .Where(value => value != null)
            .Select(value => value?.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
