using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
namespace PaperEraser;
public sealed class CategoryColorConverter : IValueConverter
{
    private static readonly string[] Strong = { "#2469D8", "#0B9168", "#D15683", "#BB771E", "#7858BB", "#16879A" };
    private static readonly string[] Light = { "#E7F1FF", "#E2F7EC", "#FDEAF1", "#FFF2DD", "#F1EAFE", "#E0F6F8" };
    internal static int ColorIndex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "未分类" || value == "全部错题" || value == "全部学科") return -1;
        if (value.Contains("数学")) return 1;
        if (value.Contains("语文")) return 0;
        if (value.Contains("英语")) return 2;
        uint hash = 2166136261; foreach (char c in value) { hash ^= c; hash *= 16777619; }
        return (int)(hash % (uint)Strong.Length);
    }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int index = ColorIndex(value as string); bool light = Equals(parameter, "light");
        var color = index < 0 ? (light ? "#EEF1F6" : "#718199") : (light ? Light[index] : Strong[index]);
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!; brush.Freeze(); return brush;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
