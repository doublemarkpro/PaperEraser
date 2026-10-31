using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PaperEraser;
internal static class UiFactory
{
    internal static DataTemplate TextTemplate(string path)
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(path));
        return new DataTemplate { VisualTree = text };
    }
    internal static void Configure(Window window)
    {
        window.Background = Brush("#FAFBFD"); window.Foreground = Brush("#172746");
        window.FontFamily = new FontFamily("Microsoft YaHei UI"); window.FontSize = 13;
        window.UseLayoutRounding = true; window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }
    internal static SolidColorBrush Brush(string value) => (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
    internal static Border Card(UIElement child)
    {
        var border = new Border { Child = child };
        if (Application.Current?.TryFindResource("Card") is Style style) border.Style = style;
        return border;
    }
    internal static void Primary(Button button)
    {
        if (Application.Current?.TryFindResource("Primary") is Style style) button.Style = style;
    }
    internal static TextBlock Hint(string text) => new() { Text = text, Foreground = Brush("#8090A9"), TextWrapping = TextWrapping.Wrap };
}
