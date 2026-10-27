using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;
namespace PaperEraser;
public sealed class CategoryIconConverter : IValueConverter
{
    private static readonly BitmapImage[] Icons = new[] { "blue", "green", "pink" }.Select(color =>
    {
        var bitmap = new BitmapImage(new Uri($"pack://application:,,,/PaperEraser;component/Assets/generated-category-{color}.png", UriKind.Absolute));
        bitmap.Freeze(); return bitmap;
    }).ToArray();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var index = CategoryColorConverter.ColorIndex(value as string); return Icons[index < 0 ? 0 : index % Icons.Length];
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
public sealed class CategoryInitialConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var name = value as string; return string.IsNullOrWhiteSpace(name) ? "?" : StringInfo.GetNextTextElement(name);
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
