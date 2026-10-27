using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PaperEraser;
public sealed class OneBasedIndexConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is int index && index >= 0 ? (index + 1).ToString(culture) : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}
