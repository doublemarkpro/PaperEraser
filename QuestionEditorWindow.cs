using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PaperEraser;
internal sealed class QuestionEditorWindow : Window
{
    private readonly QuestionItem original;
    private readonly TextBox title = new(), subject = new(), topic = new(), notes = new();
    private readonly ComboBox review = new();
    private readonly DatePicker date = new();
    internal QuestionItem? Edited { get; private set; }

    internal QuestionEditorWindow(QuestionItem item)
    {
        UiFactory.Configure(this); original = item; Title = "编辑错题"; Width = 600; Height = 820; MinWidth = 480; MinHeight = 560;
        var root = new DockPanel { Margin = new Thickness(22) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var save = new Button { Content = "保存修改", IsDefault = true, Padding = new Thickness(18, 8, 18, 8) }; UiFactory.Primary(save);
        var cancel = new Button { Content = "取消", IsCancel = true, Margin = new Thickness(0), Padding = new Thickness(18, 8, 18, 8) };
        save.Click += Save_Click; buttons.Children.Add(save); buttons.Children.Add(cancel); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var form = new StackPanel();
        form.Children.Add(new TextBlock { Text = "编辑错题", FontSize = 23, FontWeight = FontWeights.SemiBold });
        var hint = UiFactory.Hint("修改题目名称、学科和复习信息，图片内容保持不变。"); hint.Margin = new Thickness(0, 5, 0, 14); form.Children.Add(hint);
        var preview = new Border { Background = UiFactory.Brush("#F3F6FA"), CornerRadius = new CornerRadius(8), Height = 150, Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 8), Child = new Image { Source = item.Image, Stretch = Stretch.Uniform } };
        form.Children.Add(preview);
        void Add(string label, Control control) { form.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 9, 0, 5), Foreground = UiFactory.Brush("#506583") }); control.Padding = new Thickness(8, 6, 8, 6); form.Children.Add(control); }
        title.Text = item.CardTitle; subject.Text = item.Subject; topic.Text = item.Topic; notes.Text = item.Notes; notes.AcceptsReturn = true; notes.TextWrapping = TextWrapping.Wrap; notes.Height = 70;
        review.Items.Add("待复习"); review.Items.Add("复习中"); review.Items.Add("已掌握"); review.SelectedItem = item.ReviewStatus; if (review.SelectedIndex < 0) review.SelectedIndex = 0;
        date.SelectedDate = item.NextReview;
        Add("题目名称", title); Add("学科", subject); Add("知识点", topic); Add("复习状态", review); Add("下次复习日期（可留空）", date); Add("备注（可留空）", notes);
        root.Children.Add(new ScrollViewer { Content = UiFactory.Card(form), VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = root;
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(title.Text)) { MessageBox.Show("请输入题目名称。", "题目名称"); return; }
        var enteredTitle = title.Text.Trim();
        var savedLabel = enteredTitle == original.CardTitle ? original.Label : enteredTitle;
        Edited = original with { Label = savedLabel, Subject = subject.Text.Trim(), Topic = topic.Text.Trim(), ReviewStatus = review.SelectedItem as string ?? "待复习", NextReview = date.SelectedDate?.Date, Notes = notes.Text.Trim() };
        DialogResult = true;
    }
}
