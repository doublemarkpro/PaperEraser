using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PaperEraser;

internal sealed class PageDropAdorner : Adorner
{
    internal double Position { get; set; }
    internal string Label { get; set; } = "";
    internal PageDropAdorner(UIElement element) : base(element) => IsHitTestVisible = false;

    protected override void OnRender(DrawingContext drawing)
    {
        var width = AdornedElement.RenderSize.Width;
        if (width < 30) return;
        var y = Math.Clamp(Position, 4, Math.Max(4, AdornedElement.RenderSize.Height - 4));
        var blue = new SolidColorBrush(Color.FromRgb(36, 105, 235));
        var band = new SolidColorBrush(Color.FromArgb(35, 36, 105, 235));
        drawing.DrawRoundedRectangle(band, null, new Rect(4, Math.Max(0, y - 7), width - 8, 14), 4, 4);
        drawing.DrawLine(new Pen(blue, 3), new Point(7, y), new Point(width - 7, y));
        drawing.DrawEllipse(blue, null, new Point(7, y), 4, 4);
        drawing.DrawEllipse(blue, null, new Point(width - 7, y), 4, 4);
        var text = new FormattedText(Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"), 12, blue, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = Math.Max(10, width - 38), Trimming = TextTrimming.CharacterEllipsis };
        var labelY = y > text.Height + 18 ? y - text.Height - 13 : y + 10;
        drawing.DrawRoundedRectangle(Brushes.White, new Pen(blue, 1), new Rect(14, labelY - 4, text.Width + 14, text.Height + 8), 4, 4);
        drawing.DrawText(text, new Point(21, labelY));
    }
}
