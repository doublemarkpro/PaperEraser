using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PaperEraser;

internal sealed class PaperOrderList : ListBox
{
    private const string DragFormat = "PaperEraser.PaperQuestion";
    private readonly PaperSelection selection;
    private PaperQuestion? candidate, dragging;
    private Point start;
    private PageDropAdorner? marker;
    private AdornerLayer? layer;
    private readonly DispatcherTimer scrollTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    internal PaperOrderList(PaperSelection selection)
    {
        this.selection = selection;
        ItemsSource = selection.Selected; AllowDrop = true;
        ItemContainerStyle = (Style)FindResource("CollectionItem");
        BorderBrush = UiFactory.Brush("#DAE3EF");
        ScrollViewer.SetHorizontalScrollBarVisibility(this, ScrollBarVisibility.Disabled);
        var row = new FrameworkElementFactory(typeof(DockPanel));
        var remove = new FrameworkElementFactory(typeof(Button));
        remove.SetValue(DockPanel.DockProperty, Dock.Right); remove.SetValue(ContentControl.ContentProperty, "×");
        remove.SetValue(FrameworkElement.WidthProperty, 26d); remove.SetValue(FrameworkElement.HeightProperty, 26d);
        remove.SetValue(Control.FontSizeProperty, 18d); remove.SetValue(Control.PaddingProperty, new Thickness(0));
        remove.SetValue(FrameworkElement.MarginProperty, new Thickness(5, 0, 0, 0));
        remove.SetValue(Control.ForegroundProperty, UiFactory.Brush("#C24A53"));
        remove.SetValue(FrameworkElement.ToolTipProperty, "移出本次试卷（保留错题库中的题目）");
        remove.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "移出本次试卷");
        remove.AddHandler(Button.ClickEvent, new RoutedEventHandler((sender, e) =>
        {
            if (((FrameworkElement)sender).DataContext is PaperQuestion item) item.Included = false;
            e.Handled = true;
        }));
        row.AppendChild(remove);
        var handle = new FrameworkElementFactory(typeof(TextBlock)); handle.SetValue(DockPanel.DockProperty, Dock.Left);
        handle.SetValue(TextBlock.TextProperty, "⠿"); handle.SetValue(Control.ForegroundProperty, UiFactory.Brush("#8295B2"));
        handle.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 7, 0)); handle.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        row.AppendChild(handle);
        var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new Binding(nameof(PaperQuestion.Name)));
        text.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(PaperQuestion.Collection))); text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); row.AppendChild(text);
        ItemTemplate = new DataTemplate { VisualTree = row };
        PreviewMouseLeftButtonDown += ListMouseDown;
        PreviewMouseLeftButtonUp += (_, _) => candidate = null;
        PreviewMouseMove += ListMouseMove;
        DragOver += OnDragOver; Drop += OnDrop;
        DragLeave += (_, _) => ClearMarker();
        scrollTimer.Tick += (_, _) => AutoScroll();
        Unloaded += (_, _) => { scrollTimer.Stop(); ClearMarker(); };
    }
    private void ListMouseDown(object sender, MouseButtonEventArgs e)
    {
        candidate = null;
        for (var node = e.OriginalSource as DependencyObject; node is not null && node != this; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is Button) return;
            if (node is ListBoxItem row) { candidate = row.DataContext as PaperQuestion; break; }
        }
        start = e.GetPosition(this);
    }
    private void ListMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { candidate = null; return; }
        if (candidate is null || dragging is not null) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        dragging = candidate; candidate = null; SelectedItem = dragging; scrollTimer.Start();
        try { DragDrop.DoDragDrop(this, new DataObject(DragFormat, dragging), DragDropEffects.Move); }
        finally { scrollTimer.Stop(); dragging = null; ClearMarker(); }
        e.Handled = true;
    }
    private (int Gap, double Y, string Label)? DropPosition(Point point)
    {
        if (point.X < 0 || point.Y < 0 || point.X >= ActualWidth || point.Y >= ActualHeight) return null;
        var hit = InputHitTest(point) as DependencyObject;
        var row = hit is null ? null : ItemsControl.ContainerFromElement(this, hit) as ListBoxItem;
        if (row?.DataContext is PaperQuestion item)
        {
            int index = selection.Selected.IndexOf(item); double top = row.TranslatePoint(new Point(), this).Y;
            bool before = point.Y < top + row.ActualHeight / 2;
            return (index + (before ? 0 : 1), top + (before ? 0 : row.ActualHeight), $"放到第 {index + 1} 题{(before ? "前" : "后")}");
        }
        if (Items.Count > 0 && ItemContainerGenerator.ContainerFromIndex(Items.Count - 1) is ListBoxItem last)
        {
            double bottom = last.TranslatePoint(new Point(0, last.ActualHeight), this).Y;
            if (point.Y >= bottom) return (Items.Count, bottom, "放到最后");
        }
        return null;
    }
    private bool UpdateMarker(Point point)
    {
        var position = DropPosition(point);
        if (position is null) { ClearMarker(); return false; }
        if (marker is null)
        {
            layer = AdornerLayer.GetAdornerLayer(this);
            if (layer is not null) { marker = new PageDropAdorner(this); layer.Add(marker); }
        }
        if (marker is not null) { marker.Position = position.Value.Y; marker.Label = position.Value.Label; marker.InvalidateVisual(); }
        return true;
    }
    private void ClearMarker() { if (marker is not null) layer?.Remove(marker); marker = null; layer = null; }
    private void OnDragOver(object sender, DragEventArgs e)
    {
        bool valid = dragging is not null && ReferenceEquals(e.Data.GetData(DragFormat), dragging);
        e.Effects = valid && UpdateMarker(e.GetPosition(this)) ? DragDropEffects.Move : DragDropEffects.None;
        if (!valid) ClearMarker(); e.Handled = true;
    }
    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        var position = DropPosition(e.GetPosition(this));
        if (dragging is not null && ReferenceEquals(e.Data.GetData(DragFormat), dragging) && position is not null)
        {
            selection.MoveToGap(dragging, position.Value.Gap); SelectedItem = dragging; ScrollIntoView(dragging); e.Effects = DragDropEffects.Move;
        }
        ClearMarker(); e.Handled = true;
    }
    private void AutoScroll()
    {
        if (dragging is null) return;
        var point = Mouse.GetPosition(this);
        if (point.X < 0 || point.X >= ActualWidth || point.Y < 0 || point.Y >= ActualHeight) return;
        var viewer = FindViewer(this); if (viewer is null) return;
        if (point.Y < 30) viewer.LineUp(); else if (point.Y > ActualHeight - 30) viewer.LineDown(); else return;
        UpdateLayout(); UpdateMarker(point);
    }
    private static ScrollViewer? FindViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var found = FindViewer(VisualTreeHelper.GetChild(root, i)); if (found is not null) return found; }
        return null;
    }
}
