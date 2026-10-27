using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
namespace PaperEraser;
internal sealed class CollectionDropAdorner : Adorner
{
    internal bool Before { get; }
    internal CollectionDropAdorner(UIElement item, bool before) : base(item) { Before = before; IsHitTestVisible = false; }
    protected override void OnRender(DrawingContext drawing)
    {
        double y = Before ? 1 : Math.Max(1, AdornedElement.RenderSize.Height - 1);
        var brush = new SolidColorBrush(Color.FromRgb(36,105,235)); var pen = new Pen(brush, 3);
        drawing.DrawLine(pen, new Point(4,y), new Point(Math.Max(4,AdornedElement.RenderSize.Width-4),y));
        drawing.DrawEllipse(brush,null,new Point(4,y),3,3);
    }
    internal static int DestinationIndex(int from, int target, bool before)
    {
        int destination = target + (before ? 0 : 1); if (from < destination) destination--; return destination;
    }
}
