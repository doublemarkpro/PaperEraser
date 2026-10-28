using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

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
    private const string DragFormat = "PaperEraser.ExportPageOrder";
    private Point dragStart;
    private ExportPage? dragCandidate;
    private ExportPage[]? draggingPages;
    private bool collapseSelectionOnClick;
    private PageDropAdorner? dropMarker;
    private AdornerLayer? dropLayer;
    private readonly DispatcherTimer scrollTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    internal string? ExportedPath { get; private set; }

    internal PageExportWindow(IEnumerable<Worksheet> pages)
    {
        UiFactory.Configure(this); Title = "多页试卷导出 PDF";
        Width = 1050; Height = 760; MinWidth = 780; MinHeight = 540;
        orderedPages = new(pages.Where(p => p.Processed is not null).Select(p => new ExportPage(p)));
        var root = new DockPanel { Margin = new Thickness(20) };
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "选择页面并调整顺序", FontSize = 24, FontWeight = FontWeights.SemiBold });
        var hint = UiFactory.Hint("勾选需要导出的页面，拖动缩略图或名称排序；蓝色插入线表示放置位置。按住 Ctrl / Shift 可多选后一起拖动，也可使用上移 / 下移。每张图片独占一页 A4，完整保留画面。");
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
        left.Children.Add(new Border { Child = new AdornerDecorator { Child = list }, BorderBrush = UiFactory.Brush("#DAE3EF"), BorderThickness = new Thickness(1), Background = Brushes.White, Padding = new Thickness(6) });
        body.Children.Add(left);
        var previewCard = new Border { Background = UiFactory.Brush("#ECF1F7"), CornerRadius = new CornerRadius(8), Child = preview };
        Grid.SetColumn(previewCard, 1); body.Children.Add(previewCard); root.Children.Add(body);
        Content = root;
        all.Click += (_, _) => { foreach (var page in orderedPages) page.Included = true; };
        none.Click += (_, _) => { foreach (var page in orderedPages) page.Included = false; };
        up.Click += (_, _) => MoveSelection(-1); down.Click += (_, _) => MoveSelection(1);
        list.SelectionChanged += (_, _) => Refresh(); export.Click += Export_Click;
        list.AllowDrop = true;
        list.PreviewMouseLeftButtonDown += List_MouseDown;
        list.PreviewMouseLeftButtonUp += List_MouseUp;
        list.PreviewMouseMove += List_MouseMove;
        list.DragOver += List_DragOver;
        list.DragLeave += (_, e) => { if (!InsideList(e.GetPosition(list))) ClearDropMarker(); };
        list.Drop += List_Drop;
        scrollTimer.Tick += (_, _) => AutoScroll();
        Closed += (_, _) => { scrollTimer.Stop(); ClearDropMarker(); };
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

    private void List_MouseDown(object sender, MouseButtonEventArgs e)
    {
        dragCandidate = null; collapseSelectionOnClick = false;
        var source = e.OriginalSource as DependencyObject;
        if (source is null || IsCheckBox(source)) return;
        var row = ItemsControl.ContainerFromElement(list, source) as ListBoxItem;
        if (row?.DataContext is not ExportPage page) return;
        dragStart = e.GetPosition(list); dragCandidate = page;
        // Keep an existing multi-selection intact until a click (rather than a drag) completes.
        if (row.IsSelected && Keyboard.Modifiers == ModifierKeys.None)
        {
            collapseSelectionOnClick = true; list.Focus(); e.Handled = true;
        }
    }

    private bool IsCheckBox(DependencyObject source)
    {
        for (DependencyObject? element = source; element is not null && element != list;)
        {
            if (element is CheckBox) return true;
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }

    private void List_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (collapseSelectionOnClick && dragCandidate is not null) list.SelectedItem = dragCandidate;
        dragCandidate = null; collapseSelectionOnClick = false;
    }

    private void List_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { dragCandidate = null; collapseSelectionOnClick = false; return; }
        if (dragCandidate is null || draggingPages is not null) return;
        var point = e.GetPosition(list);
        if (Math.Abs(point.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        if (!list.SelectedItems.Contains(dragCandidate)) list.SelectedItem = dragCandidate;
        var selected = list.SelectedItems.Cast<ExportPage>().ToHashSet();
        draggingPages = orderedPages.Where(selected.Contains).ToArray();
        dragCandidate = null; collapseSelectionOnClick = false;
        scrollTimer.Start();
        try { DragDrop.DoDragDrop(list, new DataObject(DragFormat, draggingPages), DragDropEffects.Move); }
        finally { scrollTimer.Stop(); draggingPages = null; ClearDropMarker(); }
        e.Handled = true;
    }

    private bool InsideList(Point point) => point.X >= 0 && point.Y >= 0 && point.X < list.ActualWidth && point.Y < list.ActualHeight;

    private (int Gap, double Y, string Label)? DropPosition(Point point)
    {
        if (!InsideList(point)) return null;
        var source = list.InputHitTest(point) as DependencyObject;
        var row = source is null ? null : ItemsControl.ContainerFromElement(list, source) as ListBoxItem;
        if (row?.DataContext is ExportPage page)
        {
            int index = orderedPages.IndexOf(page);
            var top = row.TranslatePoint(new Point(0, 0), list).Y;
            bool before = point.Y < top + row.ActualHeight / 2;
            return (index + (before ? 0 : 1), top + (before ? 0 : row.ActualHeight), $"放在第 {index + 1} 页{(before ? "前" : "后")}");
        }
        // The empty area below the last row is also a valid destination.
        if (orderedPages.Count > 0 && list.ItemContainerGenerator.ContainerFromIndex(orderedPages.Count - 1) is ListBoxItem last)
        {
            var bottom = last.TranslatePoint(new Point(0, last.ActualHeight), list).Y;
            if (point.Y >= bottom) return (orderedPages.Count, bottom, "放到最后");
        }
        return null;
    }

    private bool UpdateDropMarker(Point point)
    {
        var destination = DropPosition(point);
        if (destination is null) { ClearDropMarker(); return false; }
        if (dropMarker is null)
        {
            dropLayer = AdornerLayer.GetAdornerLayer(list);
            if (dropLayer is not null) { dropMarker = new PageDropAdorner(list); dropLayer.Add(dropMarker); }
        }
        if (dropMarker is not null)
        {
            dropMarker.Position = destination.Value.Y;
            dropMarker.Label = $"移动 {draggingPages?.Length ?? 0} 页 · {destination.Value.Label}";
            dropMarker.InvalidateVisual();
        }
        return true;
    }

    private void ClearDropMarker()
    {
        if (dropMarker is not null) dropLayer?.Remove(dropMarker);
        dropMarker = null; dropLayer = null;
    }

    private void List_DragOver(object sender, DragEventArgs e)
    {
        bool valid = draggingPages is not null && ReferenceEquals(e.Data.GetData(DragFormat), draggingPages);
        e.Effects = valid && UpdateDropMarker(e.GetPosition(list)) ? DragDropEffects.Move : DragDropEffects.None;
        if (!valid) ClearDropMarker();
        e.Handled = true;
    }

    private void List_Drop(object sender, DragEventArgs e)
    {
        var destination = DropPosition(e.GetPosition(list));
        e.Effects = DragDropEffects.None;
        if (draggingPages is not null && ReferenceEquals(e.Data.GetData(DragFormat), draggingPages) && destination is not null)
        {
            MoveToGap(draggingPages, destination.Value.Gap); e.Effects = DragDropEffects.Move;
        }
        ClearDropMarker(); e.Handled = true;
    }

    private void MoveToGap(IEnumerable<ExportPage> pages, int gap)
    {
        if (gap < 0 || gap > orderedPages.Count) return;
        var moving = pages.ToHashSet();
        var items = orderedPages.Where(moving.Contains).ToArray();
        var target = orderedPages.Where(p => !moving.Contains(p)).ToList();
        int insertion = gap - orderedPages.Take(gap).Count(moving.Contains);
        target.InsertRange(insertion, items);
        for (int i = 0; i < target.Count; i++)
        {
            int current = orderedPages.IndexOf(target[i]);
            if (current != i) orderedPages.Move(current, i);
        }
        list.SelectedItems.Clear(); foreach (var page in items) list.SelectedItems.Add(page);
        if (list.SelectedItem is not null) list.ScrollIntoView(list.SelectedItem);
        Refresh();
    }

    private void AutoScroll()
    {
        if (draggingPages is null) return;
        var point = Mouse.GetPosition(list);
        if (!InsideList(point)) return;
        var viewer = FindScrollViewer(list);
        if (viewer is null) return;
        if (point.Y < 35) viewer.LineUp();
        else if (point.Y > list.ActualHeight - 35) viewer.LineDown();
        else return;
        list.UpdateLayout(); UpdateDropMarker(point);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null) return found;
        }
        return null;
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
