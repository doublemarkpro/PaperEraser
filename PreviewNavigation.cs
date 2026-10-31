using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PaperEraser;

internal sealed partial class PreviewWindow
{
    private double previewZoom = 1;
    private bool fitPreviewWidth, updatingNavigation;
    private int pageCount;
    private readonly ComboBox zoomChoice = new() { Width = 78, Height = 32, ItemsSource = new[] { 25, 50, 75, 100, 125, 150, 200, 250, 300 }, SelectedItem = 100 };
    private readonly ComboBox pageJump = new() { Width = 62, Height = 32 };
    private readonly TextBlock pageTotal = UiFactory.Hint("/ 0 页");
    private readonly TextBlock pageBadge = new() { Text = "第 0 / 0 页", Foreground = UiFactory.Brush("#245CC2"), FontWeight = FontWeights.SemiBold };
    private readonly TextBlock exportStatus = new() { Foreground = UiFactory.Brush("#246345"), TextWrapping = TextWrapping.Wrap };
    private readonly Border exportNotice = new() { Background = UiFactory.Brush("#EDFAF2"), BorderBrush = UiFactory.Brush("#CEE9D8"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };

    private UIElement CreatePreviewArea()
    {
        var root = new DockPanel();
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(tools, Dock.Top); root.Children.Add(tools);
        Button Tool(string text, Action action)
        {
            var button = new Button { Content = text, Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 6, 4) };
            button.Click += (_, _) => action(); tools.Children.Add(button); return button;
        }
        tools.Children.Add(new TextBlock { Text = "预览缩放", Foreground = UiFactory.Brush("#617694"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 4) });
        Tool("－", () => SetPreviewZoom(previewZoom / 1.25));
        zoomChoice.Margin = new Thickness(0, 0, 6, 4); tools.Children.Add(zoomChoice);
        Tool("＋", () => SetPreviewZoom(previewZoom * 1.25));
        Tool("适合宽度", () => { fitPreviewWidth = true; FitPreviewWidth(); });
        Tool("100%", () => SetPreviewZoom(1));
        Tool("上一页", () => JumpToPage(CurrentPage() - 1));
        pageJump.Margin = new Thickness(0, 0, 5, 4); tools.Children.Add(pageJump);
        pageTotal.VerticalAlignment = VerticalAlignment.Center; pageTotal.Margin = new Thickness(0, 0, 8, 4); tools.Children.Add(pageTotal);
        Tool("下一页", () => JumpToPage(CurrentPage() + 1));
        // Display percentages without making the document's layout scale editable here.
        var templateText = new FrameworkElementFactory(typeof(TextBlock));
        templateText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { StringFormat = "{0}%" });
        zoomChoice.ItemTemplate = new DataTemplate { VisualTree = templateText };
        zoomChoice.SelectionChanged += (_, _) => { if (!updatingNavigation && zoomChoice.SelectedItem is int percent) SetPreviewZoom(percent / 100.0); };
        pageJump.SelectionChanged += (_, _) => { if (!updatingNavigation && pageJump.SelectedItem is int page) JumpToPage(page); };
        var notice = new DockPanel();
        var actions = new WrapPanel { Margin = new Thickness(8, 0, 0, 0) }; DockPanel.SetDock(actions, Dock.Right); notice.Children.Add(actions);
        var open = new Button { Content = "打开 PDF", Padding = new Thickness(8, 5, 8, 5) };
        var folder = new Button { Content = "打开文件夹", Padding = new Thickness(8, 5, 8, 5) };
        actions.Children.Add(open); actions.Children.Add(folder);
        open.Click += (_, _) => OpenExport(false); folder.Click += (_, _) => OpenExport(true);
        notice.Children.Add(exportStatus); exportNotice.Child = notice; DockPanel.SetDock(exportNotice, Dock.Top); root.Children.Add(exportNotice);
        previewScroll.Content = preview;
        previewScroll.ScrollChanged += (_, e) =>
        {
            UpdatePageIndicator();
            // Scrollbars change the viewport without changing the ScrollViewer's outer size.
            if (fitPreviewWidth && Math.Abs(e.ViewportWidthChange) > .1)
                Dispatcher.BeginInvoke(() => { if (fitPreviewWidth) FitPreviewWidth(); }, DispatcherPriority.Loaded);
        };
        previewScroll.SizeChanged += (_, _) => { if (fitPreviewWidth) FitPreviewWidth(); UpdatePageIndicator(); };
        previewScroll.PreviewMouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            SetPreviewZoom(previewZoom * (e.Delta > 0 ? 1.25 : .8)); e.Handled = true;
        };
        previewScroll.ToolTip = "Ctrl + 滚轮缩放预览；缩放不会改变 PDF 排版。";
        var viewport = new Grid(); viewport.Children.Add(previewScroll);
        var badge = new Border { Child = pageBadge, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Background = UiFactory.Brush("#F2F7FF"), BorderBrush = UiFactory.Brush("#CBDEFB"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 25, 14), IsHitTestVisible = false };
        viewport.Children.Add(badge); root.Children.Add(viewport);
        return root;
    }
    private void SetPreviewZoom(double value, bool keepFit = false)
    {
        if (!keepFit) fitPreviewWidth = false;
        value = Math.Clamp(value, .25, 3);
        double oldZoom = previewZoom;
        double anchor = Math.Max(0, (previewScroll.VerticalOffset - preview.Margin.Top) / oldZoom);
        double horizontal = (previewScroll.HorizontalOffset + previewScroll.ViewportWidth / 2) / oldZoom;
        previewZoom = value; preview.LayoutTransform = new ScaleTransform(value, value);
        updatingNavigation = true;
        try
        {
            // Include the current step so +/- and fit-width always show an accurate percentage.
            int percent = (int)Math.Round(value * 100);
            zoomChoice.ItemsSource = new[] { 25, 50, 75, 100, 125, 150, 200, 250, 300, percent }.Distinct().Order().ToArray();
            zoomChoice.SelectedItem = percent;
        }
        finally { updatingNavigation = false; }
        previewScroll.UpdateLayout();
        previewScroll.ScrollToVerticalOffset(preview.Margin.Top + anchor * value);
        previewScroll.ScrollToHorizontalOffset(Math.Max(0, horizontal * value - previewScroll.ViewportWidth / 2));
        UpdatePageIndicator();
    }
    private void FitPreviewWidth()
    {
        if (previewScroll.ViewportWidth > 0) SetPreviewZoom((previewScroll.ViewportWidth - preview.Margin.Left - preview.Margin.Right - 4) / PdfExporter.PageWidth, true);
    }
    private int CurrentPage()
    {
        if (pageCount == 0) return 0;
        double center = previewScroll.VerticalOffset + Math.Min(previewScroll.ViewportHeight / 2, PdfExporter.PageHeight * previewZoom / 2);
        return Math.Clamp((int)Math.Floor((center - preview.Margin.Top) / ((PdfExporter.PageHeight + 18) * previewZoom)) + 1, 1, pageCount);
    }
    private void UpdatePageIndicator()
    {
        if (updatingNavigation) return;
        int current = CurrentPage(); pageBadge.Text = $"第 {current} / {pageCount} 页";
        updatingNavigation = true;
        try { pageJump.SelectedItem = current == 0 ? null : current; }
        finally { updatingNavigation = false; }
    }
    private void JumpToPage(int page)
    {
        if (pageCount == 0) return;
        page = Math.Clamp(page, 1, pageCount);
        previewScroll.ScrollToVerticalOffset(preview.Margin.Top + (page - 1) * (PdfExporter.PageHeight + 18) * previewZoom);
    }
    private void UpdatePageCount(int count)
    {
        pageCount = count;
        updatingNavigation = true;
        try { pageJump.ItemsSource = Enumerable.Range(1, count).ToArray(); pageTotal.Text = $"/ {count} 页"; }
        finally { updatingNavigation = false; }
        Dispatcher.BeginInvoke(() => { previewScroll.UpdateLayout(); if (fitPreviewWidth) FitPreviewWidth(); UpdatePageIndicator(); }, DispatcherPriority.Loaded);
    }
    private void OpenExport(bool folder)
    {
        if (ExportedPath is null) return;
        try
        {
            var start = folder ? new ProcessStartInfo("explorer.exe") : new ProcessStartInfo(ExportedPath) { UseShellExecute = true };
            if (folder) start.ArgumentList.Add("/select," + ExportedPath);
            Process.Start(start);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开导出文件", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
