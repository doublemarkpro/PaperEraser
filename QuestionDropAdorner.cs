using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PaperEraser;
internal sealed class QuestionDropAdorner : Adorner
{
    internal static int DestinationIndex(int from, int target, bool before)
    {
        var result = target + (before ? 0 : 1);
        if (from < result) result--;
        return result;
    }
    internal bool Before { get; }
    internal QuestionDropAdorner(UIElement adorned, bool before) : base(adorned) { Before = before; IsHitTestVisible = false; }
    protected override void OnRender(DrawingContext drawing)
    {
        var x = Before ? 2.0 : Math.Max(2, AdornedElement.RenderSize.Width - 2);
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(36, 105, 235)), 3);
        var brush = new SolidColorBrush(Color.FromRgb(36, 105, 235));
        var height = AdornedElement.RenderSize.Height;
        drawing.DrawLine(pen, new Point(x, 6), new Point(x, Math.Max(6, height - 6)));
        drawing.DrawEllipse(brush, null, new Point(x, 5), 3, 3);
        drawing.DrawEllipse(brush, null, new Point(x, Math.Max(5, height - 5)), 3, 3);
    }
}
