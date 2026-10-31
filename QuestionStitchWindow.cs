using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PaperEraser;

internal sealed class QuestionStitchWindow : Window
{
    private sealed class FragmentItem : INotifyPropertyChanged
    {
        internal QuestionFragment Fragment { get; }
        public string Label => Fragment.Label;
        public BitmapSource Image => Fragment.Image;
        private bool included = true;
        public bool Included { get => included; set { if (included == value) return; included = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Included))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        internal FragmentItem(QuestionFragment fragment) => Fragment = fragment;
    }
    private readonly ObservableCollection<FragmentItem> fragments;
    private readonly ListBox list = new() { BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel preview = new() { Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBox name = new() { Text = "跨页拼接错题", MaxLength = 80, Width = 190 };
    private readonly ComboBox gap = new() { ItemsSource = new[] { 0, 8, 12, 20, 32 }, SelectedItem = 12, Width = 65 };
    private readonly CheckBox matchWidths = new() { Content = "统一片段宽度", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock summary = UiFactory.Hint("");
    private readonly Button up = new() { Content = "上移" }, down = new() { Content = "下移" };
    private readonly Button confirm = new() { Content = "拼成一道题并加入错题集", Padding = new Thickness(16, 8, 16, 8) };
    private BitmapSource? combined;
    private bool changingChecks;
    internal BitmapSource? CombinedImage { get; private set; }
    internal IReadOnlyList<QuestionFragment> SelectedFragments { get; private set; } = Array.Empty<QuestionFragment>();
    internal string QuestionName => string.IsNullOrWhiteSpace(name.Text) ? "跨页拼接错题" : name.Text.Trim();

    internal QuestionStitchWindow(IReadOnlyList<QuestionFragment> source, string collection, string subject)
    {
        UiFactory.Configure(this); Title = "跨页拼题"; Width = 1060; Height = 800; MinWidth = 820; MinHeight = 580;
        fragments = new(source.Select(f => new FragmentItem(f)));
        var root = new DockPanel { Margin = new Thickness(20) };
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "把跨页片段拼成一道完整题目", FontSize = 23, FontWeight = FontWeights.SemiBold });
        var hint = UiFactory.Hint("勾选属于同一道题的片段，按题目上半部分 → 下半部分排序。右侧预览拼接结果；未勾选的其他题目选区会保留。");
        hint.Margin = new Thickness(0, 8, 0, 12); header.Children.Add(hint);
        var settings = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        settings.Children.Add(new TextBlock { Text = "题目名称", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        settings.Children.Add(name);
        settings.Children.Add(new TextBlock { Text = "片段间距", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 8, 0) }); settings.Children.Add(gap);
        matchWidths.Margin = new Thickness(14, 0, 0, 0); settings.Children.Add(matchWidths); header.Children.Add(settings);
        var target = UiFactory.Hint($"加入：{collection} · {(string.IsNullOrWhiteSpace(subject) ? "未分类" : subject)}。默认保留片段相对尺寸、左对齐；两页拍摄比例不同时，可试用统一宽度。");
        target.Margin = new Thickness(0, 0, 0, 12); header.Children.Add(target);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        UiFactory.Primary(confirm); DockPanel.SetDock(confirm, Dock.Right); footer.Children.Add(confirm);
        summary.VerticalAlignment = VerticalAlignment.Center; footer.Children.Add(summary); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new DockPanel { Margin = new Thickness(0, 0, 14, 0) };
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var all = new Button { Content = "全选" }; var none = new Button { Content = "全不选" };
        actions.Children.Add(all); actions.Children.Add(none); actions.Children.Add(up); actions.Children.Add(down);
        DockPanel.SetDock(actions, Dock.Top); left.Children.Add(actions);
        list.ItemsSource = fragments; list.ItemTemplate = CreateFragmentTemplate();
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        var style = new Style(typeof(ListBoxItem)); style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)); list.ItemContainerStyle = style;
        left.Children.Add(new Border { Child = list, Background = Brushes.White, BorderThickness = new Thickness(1), BorderBrush = UiFactory.Brush("#DAE3EF"), Padding = new Thickness(5) }); body.Children.Add(left);
        var scroll = new ScrollViewer { Content = preview, Background = UiFactory.Brush("#ECF1F7"), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1); body.Children.Add(scroll); root.Children.Add(body); Content = root;
        all.Click += (_, _) => SetIncluded(true);
        none.Click += (_, _) => SetIncluded(false);
        up.Click += (_, _) => Move(-1); down.Click += (_, _) => Move(1);
        list.SelectionChanged += (_, _) => RefreshButtons();
        foreach (var fragment in fragments) fragment.PropertyChanged += (_, _) => UpdatePreview();
        gap.SelectionChanged += (_, _) => UpdatePreview(); matchWidths.Checked += (_, _) => UpdatePreview(); matchWidths.Unchecked += (_, _) => UpdatePreview();
        confirm.Click += (_, _) =>
        {
            if (combined is null) return;
            CombinedImage = combined; SelectedFragments = fragments.Where(f => f.Included).Select(f => f.Fragment).ToArray(); DialogResult = true;
        };
        list.SelectedIndex = fragments.Count > 0 ? 0 : -1; UpdatePreview();
    }

    private static DataTemplate CreateFragmentTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(DockPanel)); panel.SetValue(FrameworkElement.MarginProperty, new Thickness(6));
        var check = new FrameworkElementFactory(typeof(CheckBox)); check.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(FragmentItem.Included)) { Mode = BindingMode.TwoWay });
        check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); check.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0)); panel.AppendChild(check);
        var image = new FrameworkElementFactory(typeof(Image)); image.SetBinding(Image.SourceProperty, new Binding(nameof(FragmentItem.Image))); image.SetValue(FrameworkElement.WidthProperty, 66.0); image.SetValue(FrameworkElement.HeightProperty, 60.0); image.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 12, 0)); panel.AppendChild(image);
        var label = new FrameworkElementFactory(typeof(TextBlock)); label.SetBinding(TextBlock.TextProperty, new Binding(nameof(FragmentItem.Label))); label.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(FragmentItem.Label))); label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); panel.AppendChild(label);
        return new DataTemplate { VisualTree = panel };
    }
    private void Move(int direction)
    {
        int from = list.SelectedIndex, to = from + direction;
        if (from < 0 || to < 0 || to >= fragments.Count) return;
        var item = fragments[from]; fragments.Move(from, to); list.SelectedItem = item; list.ScrollIntoView(item); UpdatePreview();
    }
    private void RefreshButtons() { up.IsEnabled = list.SelectedIndex > 0; down.IsEnabled = list.SelectedIndex >= 0 && list.SelectedIndex < fragments.Count - 1; }
    private void SetIncluded(bool included)
    {
        changingChecks = true;
        try { foreach (var fragment in fragments) fragment.Included = included; }
        finally { changingChecks = false; }
        UpdatePreview();
    }
    private void UpdatePreview()
    {
        if (changingChecks) return;
        combined = null; confirm.IsEnabled = false; preview.Children.Clear(); RefreshButtons();
        var selected = fragments.Where(f => f.Included).ToArray();
        try
        {
            combined = QuestionStitcher.Combine(selected.Select(f => f.Image).ToArray(), (int)(gap.SelectedItem ?? 12), matchWidths.IsChecked == true);
            preview.Children.Add(new Image { Source = combined, Width = Math.Min(520, combined.PixelWidth), Stretch = Stretch.Uniform });
            summary.Text = $"{selected.Length} 个片段 → 1 道题 · {combined.PixelWidth} × {combined.PixelHeight} 像素"; confirm.IsEnabled = true;
        }
        catch (ArgumentException ex) { summary.Text = ex.Message; }
    }
}
