using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace PaperEraser;

internal sealed class PageExportWindow : Window
{
    private sealed class ExportPage : INotifyPropertyChanged
    {
        public Worksheet Page { get; }
        public string Title => Page.Title;
        public System.Windows.Media.Imaging.BitmapSource? Image => Page.Processed;
        private bool included = true;
        public bool Included { get => included; set { if (included == value) return; included = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Included))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        public ExportPage(Worksheet page) => Page = page;
    }
    private readonly ObservableCollection<ExportPage> orderedPages;
    private readonly ListBox list = new() { SelectionMode = SelectionMode.Extended, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly Image preview = new() { Stretch = Stretch.Uniform, Margin = new Thickness(16) };
    private readonly TextBlock summary = UiFactory.Hint("");
    private readonly Button up = new() { Content = "上移", Padding = new Thickness(12, 6, 12, 6) };
    private readonly Button down = new() { Content = "下移", Padding = new Thickness(12, 6, 12, 6) };
    private readonly Button export = new() { Content = "导出 PDF", Padding = new Thickness(18, 8, 18, 8) };
    internal string? ExportedPath { get; private set; }

    internal PageExportWindow(IEnumerable<Worksheet> pages)
    {
        UiFactory.Configure(this); Title = "多页试卷导出 PDF";
        Width = 1050; Height = 760; MinWidth = 780; MinHeight = 540;
        orderedPages = new(pages.Where(p => p.Processed is not null).Select(p => new ExportPage(p)));
        var root = new DockPanel { Margin = new Thickness(20) };
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "选择页面并调整顺序", FontSize = 24, FontWeight = FontWeights.SemiBold });
        var hint = UiFactory.Hint("勾选需要导出的页面。点击行后用上移 / 下移排序，按住 Ctrl 或 Shift 可同时移动多行。PDF 按列表从上到下导出，每张图片独占一页 A4，完整保留画面。");
        hint.Margin = new Thickness(0, 8, 0, 16); header.Children.Add(hint);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        UiFactory.Primary(export); DockPanel.SetDock(export, Dock.Right); footer.Children.Add(export);
        summary.VerticalAlignment = VerticalAlignment.Center; footer.Children.Add(summary);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(370) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new DockPanel { Margin = new Thickness(0, 0, 14, 0) };
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var all = new Button { Content = "全部勾选", Padding = new Thickness(12, 6, 12, 6) };
        var none = new Button { Content = "全部取消", Padding = new Thickness(12, 6, 12, 6) };
        actions.Children.Add(all); actions.Children.Add(none); actions.Children.Add(up); actions.Children.Add(down);
        DockPanel.SetDock(actions, Dock.Top); left.Children.Add(actions);
        list.ItemsSource = orderedPages; list.AlternationCount = int.MaxValue;
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.ItemContainerStyle = new Style(typeof(ListBoxItem));
        list.ItemContainerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        list.ItemTemplate = CreateTemplate();
        left.Children.Add(new Border { Child = list, BorderBrush = UiFactory.Brush("#DAE3EF"), BorderThickness = new Thickness(1), Background = Brushes.White, Padding = new Thickness(6) });
        body.Children.Add(left);
        var previewCard = new Border { Background = UiFactory.Brush("#ECF1F7"), CornerRadius = new CornerRadius(8), Child = preview };
        Grid.SetColumn(previewCard, 1); body.Children.Add(previewCard); root.Children.Add(body);
        Content = root;
        all.Click += (_, _) => { foreach (var page in orderedPages) page.Included = true; };
        none.Click += (_, _) => { foreach (var page in orderedPages) page.Included = false; };
        up.Click += (_, _) => MoveSelection(-1); down.Click += (_, _) => MoveSelection(1);
        list.SelectionChanged += (_, _) => Refresh(); export.Click += Export_Click;
        foreach (var page in orderedPages) page.PropertyChanged += (_, _) => Refresh();
        if (orderedPages.Count > 0) list.SelectedIndex = 0;
        Refresh();
    }

    private static DataTemplate CreateTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(DockPanel)); panel.SetValue(FrameworkElement.MarginProperty, new Thickness(6));
        var check = new FrameworkElementFactory(typeof(CheckBox));
        check.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(ExportPage.Included)) { Mode = BindingMode.TwoWay });
        check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); check.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
        check.SetValue(FrameworkElement.ToolTipProperty, "勾选后加入 PDF"); panel.AppendChild(check);
        var number = new FrameworkElementFactory(typeof(TextBlock));
        number.SetBinding(TextBlock.TextProperty, new Binding("(ItemsControl.AlternationIndex)") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1), Converter = new OneBasedIndexConverter() });
        number.SetValue(FrameworkElement.WidthProperty, 32.0); number.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); panel.AppendChild(number);
        var image = new FrameworkElementFactory(typeof(Image)); image.SetBinding(Image.SourceProperty, new Binding(nameof(ExportPage.Image)));
        image.SetValue(FrameworkElement.WidthProperty, 48.0); image.SetValue(FrameworkElement.HeightProperty, 68.0); image.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 12, 0)); panel.AppendChild(image);
        var label = new FrameworkElementFactory(typeof(TextBlock)); label.SetBinding(TextBlock.TextProperty, new Binding(nameof(ExportPage.Title)));
        label.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(ExportPage.Title)));
        label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); panel.AppendChild(label);
        return new DataTemplate { VisualTree = panel };
    }

    private void Refresh()
    {
        var selected = list.SelectedItems.Cast<ExportPage>().ToHashSet();
        var count = orderedPages.Count(p => p.Included);
        summary.Text = $"共 {orderedPages.Count} 张已处理图片 · 已勾选 {count} 张 · 导出 {count} 页";
        preview.Source = (list.SelectedItem as ExportPage)?.Image;
        export.IsEnabled = count > 0;
        up.IsEnabled = Enumerable.Range(1, Math.Max(0, orderedPages.Count - 1)).Any(i => selected.Contains(orderedPages[i]) && !selected.Contains(orderedPages[i - 1]));
        down.IsEnabled = Enumerable.Range(0, Math.Max(0, orderedPages.Count - 1)).Any(i => selected.Contains(orderedPages[i]) && !selected.Contains(orderedPages[i + 1]));
    }

    private void MoveSelection(int direction)
    {
        var selected = list.SelectedItems.Cast<ExportPage>().ToHashSet();
        if (direction < 0)
        {
            for (int i = 1; i < orderedPages.Count; i++)
                if (selected.Contains(orderedPages[i]) && !selected.Contains(orderedPages[i - 1])) orderedPages.Move(i, i - 1);
        }
        else
        {
            for (int i = orderedPages.Count - 2; i >= 0; i--)
                if (selected.Contains(orderedPages[i]) && !selected.Contains(orderedPages[i + 1])) orderedPages.Move(i, i + 1);
        }
        list.SelectedItems.Clear(); foreach (var page in orderedPages.Where(selected.Contains)) list.SelectedItems.Add(page);
        if (list.SelectedItem is not null) list.ScrollIntoView(list.SelectedItem);
        Refresh();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var images = orderedPages.Where(p => p.Included).Select(p => p.Image!).ToArray();
        if (images.Length == 0) return;
        var dialog = new SaveFileDialog { Filter = "PDF 文档|*.pdf", DefaultExt = ".pdf", FileName = "试卷合集.pdf" };
        if (dialog.ShowDialog(this) != true) return;
        try { PdfExporter.SavePages(dialog.FileName, images); ExportedPath = dialog.FileName; DialogResult = true; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
