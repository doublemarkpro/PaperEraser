using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PaperEraser;

internal sealed class QuestionImagePreviewWindow : Window
{
    private enum ViewMode { Manual, FitWidth, FitWindow }
    private const double MinZoom = 0.01, MaxZoom = 8;
    private readonly BitmapSource source;
    private readonly Image image = new() { Stretch = Stretch.Fill };
    private readonly ScrollViewer scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = UiFactory.Brush("#ECF1F7") };
    private readonly TextBlock zoomText = new() { Width = 58, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button zoomIn = new() { Content = "＋ 放大" }, zoomOut = new() { Content = "－ 缩小" };
    private double zoom = 1;
    private ViewMode mode = ViewMode.FitWidth;
    private bool fitPending, closed;

    internal QuestionImagePreviewWindow(BitmapSource source, string questionName)
    {
        this.source = source;
        UiFactory.Configure(this); Title = "题目放大预览 · " + questionName;
        Width = 1080; Height = 820; MinWidth = 560; MinHeight = 400;
        var root = new DockPanel { Margin = new Thickness(16) };
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var close = new Button { Content = "关闭预览", Margin = new Thickness(0), Padding = new Thickness(15, 8, 15, 8) };
        close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Right); toolbar.Children.Add(close);
        var controls = new WrapPanel();
        var actual = new Button { Content = "100%" };
        var width = new Button { Content = "适合宽度" };
        var fit = new Button { Content = "查看全图" };
        controls.Children.Add(zoomOut); controls.Children.Add(zoomText); controls.Children.Add(zoomIn); controls.Children.Add(actual); controls.Children.Add(width); controls.Children.Add(fit);
        toolbar.Children.Add(controls); DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        var hint = UiFactory.Hint("滚轮上下浏览，Ctrl + 滚轮缩放；Esc 关闭。可把预览窗口移到一旁，对照题目填写错因和备注。");
        hint.Margin = new Thickness(0, 0, 0, 10); DockPanel.SetDock(hint, Dock.Top); root.Children.Add(hint);
        image.Source = source;
        scroll.Content = new Border { Child = image, Background = Brushes.White, Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        root.Children.Add(scroll); Content = root;
        zoomIn.Click += (_, _) => ZoomBy(1.25);
        zoomOut.Click += (_, _) => ZoomBy(1 / 1.25);
        actual.Click += (_, _) => { mode = ViewMode.Manual; SetZoom(1); };
        width.Click += (_, _) => SetFit(ViewMode.FitWidth);
        fit.Click += (_, _) => SetFit(ViewMode.FitWindow);
        scroll.PreviewMouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            ZoomBy(e.Delta > 0 ? 1.25 : 1 / 1.25, e.GetPosition(scroll)); e.Handled = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        };
        Loaded += (_, _) => SetFit(ViewMode.FitWidth);
        scroll.SizeChanged += (_, _) => ScheduleFit();
        scroll.ScrollChanged += (_, e) => { if (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0) ScheduleFit(); };
        Closed += (_, _) => closed = true;
        SetZoom(1, preservePosition: false);
    }

    private void ZoomBy(double factor, Point? anchor = null)
    {
        mode = ViewMode.Manual; SetZoom(zoom * factor, anchor);
    }

    private void SetZoom(double value, Point? anchor = null, bool preservePosition = true)
    {
        value = Math.Clamp(value, MinZoom, MaxZoom);
        var point = anchor ?? new Point(scroll.ViewportWidth / 2, scroll.ViewportHeight / 2);
        double x = (scroll.HorizontalOffset + point.X - 16) / zoom;
        double y = (scroll.VerticalOffset + point.Y - 16) / zoom;
        zoom = value; image.Width = source.PixelWidth * zoom; image.Height = source.PixelHeight * zoom;
        zoomText.Text = $"{zoom * 100:0.#}%";
        zoomIn.IsEnabled = zoom < MaxZoom; zoomOut.IsEnabled = zoom > MinZoom;
        if (preservePosition && IsLoaded)
        {
            scroll.UpdateLayout();
            scroll.ScrollToHorizontalOffset(Math.Max(0, x * zoom + 16 - point.X));
            scroll.ScrollToVerticalOffset(Math.Max(0, y * zoom + 16 - point.Y));
        }
    }

    private void SetFit(ViewMode target)
    {
        mode = target; ApplyFit(false); scroll.ScrollToTop(); scroll.ScrollToLeftEnd();
    }

    private void ApplyFit(bool preservePosition)
    {
        if (mode == ViewMode.Manual || scroll.ViewportWidth <= 32 || scroll.ViewportHeight <= 32) return;
        double value = (scroll.ViewportWidth - 32) / source.PixelWidth;
        if (mode == ViewMode.FitWindow) value = Math.Min(value, (scroll.ViewportHeight - 32) / source.PixelHeight);
        SetZoom(value, preservePosition: preservePosition);
    }

    private void ScheduleFit()
    {
        if (fitPending || closed || mode == ViewMode.Manual || !IsLoaded) return;
        fitPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            fitPending = false;
            if (!closed) ApplyFit(true);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }
}
