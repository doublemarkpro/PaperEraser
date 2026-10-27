using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PaperEraser;
internal sealed class PreviewWindow : Window
{
    private readonly IReadOnlyList<BitmapSource> images;
    private readonly TextBox title = new() { Text = "错题练习", MaxLength = 50, Width = 210, Height = 32 };
    private readonly ComboBox scale = new() { Width = 78, Height = 32, ItemsSource = new[] { 40, 50, 60, 70, 80, 90, 100 }, SelectedItem = 100 };
    private readonly ComboBox gap = new() { Width = 70, Height = 32, ItemsSource = new[] { 0, 3, 5, 8, 10, 15, 20, 30 }, SelectedItem = 5 };
    private readonly ComboBox blank = new() { Width = 70, Height = 32, ItemsSource = new[] { 0, 5, 10, 15, 20, 30, 40, 50 }, SelectedItem = 0 };
    private readonly ComboBox margins = new() { Width = 70, Height = 32, ItemsSource = new[] { 8, 10, 13, 15, 20, 25 }, SelectedItem = 13 };
    private readonly StackPanel preview = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(16, 8, 16, 16) };
    private readonly TextBlock summary = UiFactory.Hint("");
    private bool ready;
    internal string? ExportedPath { get; private set; }
    internal PreviewWindow(IReadOnlyList<BitmapSource> images)
    {
        UiFactory.Configure(this); this.images = images; Title = "预览与导出"; Width = 940; Height = 900; MinWidth = 680; MinHeight = 540;
        var root = new DockPanel { Margin = new Thickness(18) };
        var header = new StackPanel();
        var heading = new DockPanel();
        var export = new Button { Content = "导出 PDF", Padding = new Thickness(16, 8, 16, 8) }; UiFactory.Primary(export); DockPanel.SetDock(export, Dock.Right); heading.Children.Add(export);
        heading.Children.Add(new TextBlock { Text = "预览与导出", FontSize = 23, FontWeight = FontWeights.SemiBold }); header.Children.Add(heading);
        var hint = UiFactory.Hint("调整排版后，预览与导出使用相同设置；尺寸、间距和留白单位为毫米。"); hint.Margin = new Thickness(0, 5, 0, 12); header.Children.Add(hint);
        var controls = new WrapPanel();
        void Add(string label, Control control) { controls.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = UiFactory.Brush("#617694") }); control.Margin = new Thickness(0, 0, 12, 6); controls.Children.Add(control); }
        Add("标题", title); Add("大小 %", scale); Add("间距", gap); Add("答题留白", blank); Add("页边距", margins);
        header.Children.Add(controls); summary.Margin = new Thickness(0, 5, 0, 10); header.Children.Add(summary);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        root.Children.Add(new ScrollViewer { Content = preview, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Background = UiFactory.Brush("#ECF1F7") });
        Content = root; export.Click += Export_Click;
        title.TextChanged += (_, _) => UpdatePreview(); scale.SelectionChanged += (_, _) => UpdatePreview(); gap.SelectionChanged += (_, _) => UpdatePreview(); blank.SelectionChanged += (_, _) => UpdatePreview(); margins.SelectionChanged += (_, _) => UpdatePreview();
        ready = true; UpdatePreview();
    }
    private PdfExporter.LayoutOptions Options() => new(title.Text.Trim(), (int)(margins.SelectedItem ?? 13), (int)(gap.SelectedItem ?? 5), (int)(blank.SelectedItem ?? 0), (int)(scale.SelectedItem ?? 100));
    private void UpdatePreview()
    {
        if (!ready) return;
        try
        {
            var layout = PdfExporter.Layout(images, Options()); preview.Children.Clear();
            foreach (var page in layout)
            {
                var canvas = new Canvas { Width = PdfExporter.PageWidth, Height = PdfExporter.PageHeight, Background = Brushes.White, Margin = new Thickness(0, 0, 0, 18) };
                foreach (var item in page)
                {
                    var image = new Image { Source = item.Image, Width = item.Width, Height = item.Height, Stretch = Stretch.Fill };
                    Canvas.SetLeft(image, item.X); Canvas.SetTop(image, PdfExporter.PageHeight - item.Y - item.Height); canvas.Children.Add(image);
                }
                preview.Children.Add(canvas);
            }
            summary.Text = $"{images.Count} 道题 · {layout.Count} 页 · A4 单列";
        }
        catch (Exception ex) { summary.Text = "无法预览：" + ex.Message; }
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "PDF 文档|*.pdf", FileName = "错题练习.pdf", DefaultExt = ".pdf" };
        if (dialog.ShowDialog() != true) return;
        try { PdfExporter.Save(dialog.FileName, images, Options()); ExportedPath = dialog.FileName; DialogResult = true; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
