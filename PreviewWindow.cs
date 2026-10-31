using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PaperEraser;
internal sealed partial class PreviewWindow : Window
{
    private IReadOnlyList<BitmapSource> images;
    private PaperSelection? selection;
    private bool updatingSelection;
    private readonly Button exportButton;
    private readonly TextBox title = new() { Text = "错题练习", MaxLength = 50, Width = 210, Height = 32 };
    private readonly ComboBox scale = new() { Width = 78, Height = 32, ItemsSource = new[] { 40, 50, 60, 70, 80, 90, 100 }, SelectedItem = 100 };
    private readonly ComboBox gap = new() { Width = 70, Height = 32, ItemsSource = new[] { 0, 3, 5, 8, 10, 15, 20, 30 }, SelectedItem = 5 };
    private readonly ComboBox blank = new() { Width = 70, Height = 32, ItemsSource = new[] { 0, 5, 10, 15, 20, 30, 40, 50 }, SelectedItem = 0 };
    private readonly ComboBox margins = new() { Width = 70, Height = 32, ItemsSource = new[] { 8, 10, 13, 15, 20, 25 }, SelectedItem = 13 };
    private readonly ComboBox columns = new() { Width = 100, Height = 32, ItemsSource = new[] { "一页单列", "一页两列" }, SelectedIndex = 0 };
    private readonly CheckBox pageNumbers = new() { Content = "显示页码", VerticalContentAlignment = VerticalAlignment.Center, Height = 32 };
    private readonly ComboBox numberAlignment = new() { Width = 85, Height = 32, ItemsSource = new[] { "左侧", "居中", "右侧" }, SelectedIndex = 1, IsEnabled = false };
    private readonly ComboBox numberStyle = new() { Width = 145, Height = 32, ItemsSource = new[] { "仅页码", "页码 / 总页数" }, SelectedIndex = 1, IsEnabled = false };
    private readonly StackPanel preview = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(16, 8, 16, 16) };
    private readonly TextBlock summary = UiFactory.Hint("");
    private readonly ScrollViewer previewScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Background = UiFactory.Brush("#ECF1F7") };
    private bool ready;
    internal string? ExportedPath { get; private set; }
    internal PreviewWindow(IReadOnlyList<BitmapSource> images)
    {
        UiFactory.Configure(this); this.images = images; Title = "预览与导出"; Width = 940; Height = 900; MinWidth = 680; MinHeight = 540;
        var root = new DockPanel { Margin = new Thickness(18) };
        var header = new StackPanel();
        var heading = new DockPanel();
        var export = new Button { Content = "导出 PDF", Padding = new Thickness(16, 8, 16, 8) }; UiFactory.Primary(export); DockPanel.SetDock(export, Dock.Right); heading.Children.Add(export);
        exportButton = export;
        heading.Children.Add(new TextBlock { Text = "预览与导出", FontSize = 23, FontWeight = FontWeights.SemiBold }); header.Children.Add(heading);
        var hint = UiFactory.Hint("可切换错题集逐题勾选，已选题目会保留；随机乱序仅影响本次组卷。预览与导出使用同一顺序；尺寸、间距和留白单位为毫米。"); hint.Margin = new Thickness(0, 5, 0, 12); header.Children.Add(hint);
        var controls = new WrapPanel();
        void Add(string label, Control control) { controls.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = UiFactory.Brush("#617694") }); control.Margin = new Thickness(0, 0, 12, 6); controls.Children.Add(control); }
        Add("标题", title); Add("排版", columns); Add("列内大小 %", scale); Add("间距", gap); Add("答题留白", blank); Add("页边距", margins);
        columns.ToolTip = "两列按左列从上到下，再到右列；列间距 8 毫米，大小百分比相对每列可用宽度。";
        header.Children.Add(controls); summary.Margin = new Thickness(0, 5, 0, 10); header.Children.Add(summary);
        var footerControls = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        pageNumbers.Margin = new Thickness(0, 0, 16, 0); footerControls.Children.Add(pageNumbers);
        footerControls.Children.Add(new TextBlock { Text = "页脚位置", VerticalAlignment = VerticalAlignment.Center, Foreground = UiFactory.Brush("#617694"), Margin = new Thickness(0, 0, 6, 0) });
        numberAlignment.Margin = new Thickness(0, 0, 12, 0); footerControls.Children.Add(numberAlignment);
        footerControls.Children.Add(new TextBlock { Text = "格式", VerticalAlignment = VerticalAlignment.Center, Foreground = UiFactory.Brush("#617694"), Margin = new Thickness(0, 0, 6, 0) });
        footerControls.Children.Add(numberStyle); header.Children.Insert(header.Children.Count - 1, footerControls);
        pageNumbers.ToolTip = "在每页底部添加页码，预览与导出的 PDF 同时生效。";
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        root.Children.Add(CreatePreviewArea());
        Content = root; export.Click += Export_Click;
        title.TextChanged += (_, _) => UpdatePreview(); scale.SelectionChanged += (_, _) => UpdatePreview(); gap.SelectionChanged += (_, _) => UpdatePreview(); blank.SelectionChanged += (_, _) => UpdatePreview(); margins.SelectionChanged += (_, _) => UpdatePreview();
        columns.SelectionChanged += (_, _) => UpdatePreview();
        void FooterChanged() { numberAlignment.IsEnabled = numberStyle.IsEnabled = pageNumbers.IsChecked == true; UpdatePreview(); }
        pageNumbers.Checked += (_, _) => FooterChanged(); pageNumbers.Unchecked += (_, _) => FooterChanged();
        numberAlignment.SelectionChanged += (_, _) => UpdatePreview(); numberStyle.SelectionChanged += (_, _) => UpdatePreview();
        ready = true; UpdatePreview();
    }
    internal PreviewWindow(IReadOnlyList<QuestionItem> questions, LibraryConfig library, IReadOnlyList<QuestionItem> initial)
        : this(initial.Select(q => q.Image).ToArray())
    {
        Width = 1280; MinWidth = 960;
        selection = new PaperSelection(questions, library, initial);
        var root = (DockPanel)Content;
        var viewer = root.Children[root.Children.Count - 1]; root.Children.Remove(viewer);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var panel = new DockPanel { Margin = new Thickness(0, 0, 14, 0) };
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = "跨错题集选题", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        var filter = new ComboBox { ItemsSource = new[] { new CollectionEntry(LibraryCatalog.AllCollections, "全部错题集", "") }.Concat(LibraryCatalog.Entries(library)).ToArray(), ItemTemplate = UiFactory.TextTemplate(nameof(CollectionEntry.Display)), SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 8) };
        heading.Children.Add(filter);
        var view = new System.Windows.Data.ListCollectionView(selection.Questions.ToArray());
        view.Filter = value => filter.SelectedItem is not CollectionEntry scope || scope.Id == LibraryCatalog.AllCollections || ((PaperQuestion)value).Question.Collection == scope.Id;
        filter.SelectionChanged += (_, _) => view.Refresh();
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) }; heading.Children.Add(actions);
        var select = new Button { Content = "勾选本集", Padding = new Thickness(8, 5, 8, 5) };
        var clear = new Button { Content = "取消本集", Padding = new Thickness(8, 5, 8, 5) };
        actions.Children.Add(select); actions.Children.Add(clear);
        void SetVisible(bool included) { updatingSelection = true; try { foreach (PaperQuestion item in view) item.Included = included; } finally { updatingSelection = false; } RefreshSelection(); }
        select.Click += (_, _) => SetVisible(true); clear.Click += (_, _) => SetVisible(false);
        var source = new ListBox { ItemsSource = view, BorderThickness = new Thickness(0), Background = Brushes.Transparent, ItemContainerStyle = (Style)FindResource("CollectionItem") };
        var check = new FrameworkElementFactory(typeof(CheckBox)); check.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding(nameof(PaperQuestion.Included)) { Mode = System.Windows.Data.BindingMode.TwoWay });
        check.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 5, 2, 5)); check.SetValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch);
        var row = new FrameworkElementFactory(typeof(StackPanel)); row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var thumbnail = new FrameworkElementFactory(typeof(Image)); thumbnail.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(PaperQuestion.Image))); thumbnail.SetValue(FrameworkElement.WidthProperty, 55d); thumbnail.SetValue(FrameworkElement.HeightProperty, 55d); thumbnail.SetValue(FrameworkElement.MarginProperty, new Thickness(5, 0, 8, 0)); row.AppendChild(thumbnail);
        var label = new FrameworkElementFactory(typeof(TextBlock)); label.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(PaperQuestion.Name))); label.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding(nameof(PaperQuestion.Collection))); label.SetValue(FrameworkElement.MaxWidthProperty, 185d); label.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); row.AppendChild(label); check.AppendChild(row);
        source.ItemTemplate = new DataTemplate { VisualTree = check };
        var sections = new Grid(); sections.RowDefinitions.Add(new RowDefinition()); sections.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); sections.RowDefinitions.Add(new RowDefinition());
        sections.Children.Add(source);
        var orderHeader = new StackPanel { Margin = new Thickness(0, 12, 0, 6) }; Grid.SetRow(orderHeader, 1); sections.Children.Add(orderHeader);
        orderHeader.Children.Add(new TextBlock { Text = "已选题目 · 拖动排序，× 移出本次试卷", FontWeight = FontWeights.SemiBold });
        var orderActions = new WrapPanel { Margin = new Thickness(0, 7, 0, 4) }; orderHeader.Children.Add(orderActions);
        var shuffle = new Button { Content = "随机乱序", Padding = new Thickness(8, 5, 8, 5) };
        var restore = new Button { Content = "恢复原序", Padding = new Thickness(8, 5, 8, 5) };
        orderActions.Children.Add(shuffle); orderActions.Children.Add(restore);
        var ordered = new PaperOrderList(selection); Grid.SetRow(ordered, 2); sections.Children.Add(ordered);
        shuffle.Click += (_, _) => { updatingSelection = true; try { selection.Shuffle(); } finally { updatingSelection = false; } RefreshSelection(); };
        restore.Click += (_, _) => { updatingSelection = true; try { selection.RestoreOrder(); } finally { updatingSelection = false; } RefreshSelection(); };
        selection.Selected.CollectionChanged += (_, _) => { if (!updatingSelection) RefreshSelection(); };
        panel.Children.Add(sections); grid.Children.Add(panel); Grid.SetColumn(viewer, 1); grid.Children.Add(viewer); root.Children.Add(grid);
        RefreshSelection();
    }
    private void RefreshSelection()
    {
        if (selection is null) return;
        images = selection.Selected.Select(q => q.Image).ToArray(); UpdatePreview();
    }
    private PdfExporter.LayoutOptions Options() => new(title.Text.Trim(), (int)(margins.SelectedItem ?? 13), (int)(gap.SelectedItem ?? 5), (int)(blank.SelectedItem ?? 0), (int)(scale.SelectedItem ?? 100), columns.SelectedIndex == 1 ? 2 : 1, pageNumbers.IsChecked == true, (PdfExporter.FooterAlignment)numberAlignment.SelectedIndex, (PdfExporter.PageNumberStyle)numberStyle.SelectedIndex);
    private void UpdatePreview()
    {
        if (!ready) return;
        exportButton.IsEnabled = images.Count > 0;
        if (images.Count == 0) { preview.Children.Clear(); UpdatePageCount(0); summary.Text = "尚未选题：可切换错题集勾选题目，选择会保留。"; return; }
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
            summary.Text = $"{images.Count} 道题 · {layout.Count} 页 · A4 {(columns.SelectedIndex == 1 ? "双列（先左后右）" : "单列")}";
            UpdatePageCount(layout.Count);
        }
        catch (Exception ex) { summary.Text = "无法预览：" + ex.Message; }
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (images.Count == 0) return;
        var dialog = new SaveFileDialog { Filter = "PDF 文档|*.pdf", FileName = ExportFileName.FromTitle(title.Text), DefaultExt = ".pdf" };
        if (dialog.ShowDialog(this) != true) return;
        try { SavePdf(dialog.FileName); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void SavePdf(string path)
    {
        PdfExporter.Save(path, images, Options());
        ExportedPath = path;
        exportStatus.Text = $"✓ 上次导出成功（{images.Count} 道题，{pageCount} 页）\n{path}";
        exportNotice.Visibility = Visibility.Visible;
    }
}
