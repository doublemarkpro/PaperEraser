using System.Windows;
using System.Windows.Controls;

namespace PaperEraser;

internal sealed class CollectionSettingsWindow : Window
{
    private readonly TextBox name = new() { MaxLength = 80 };
    private readonly ComboBox subject = new() { Height = 34 };
    internal string CollectionName => name.Text.Trim();
    internal string Subject => LibraryCatalog.SubjectKey(subject.SelectedItem as string);
    internal CollectionSettingsWindow(LibraryConfig config, string initialSubject, string? id = null, int questionCount = 0)
    {
        UiFactory.Configure(this); Title = id is null ? "新建错题集" : "编辑错题集 / 设置学科";
        Width = 500; Height = 365; ResizeMode = ResizeMode.NoResize;
        var root = new DockPanel { Margin = new Thickness(22) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var ok = new Button { Content = "保存", IsDefault = true }; UiFactory.Primary(ok);
        var cancel = new Button { Content = "取消", IsCancel = true }; buttons.Children.Add(ok); buttons.Children.Add(cancel); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var form = new StackPanel(); form.Children.Add(new TextBlock { Text = Title, FontSize = 21, FontWeight = FontWeights.SemiBold });
        var hint = UiFactory.Hint(id is null ? "先选择学科，再填写分类名称，例如“同步第一单元”。不同学科可以使用相同名称。" : $"修改名称或归属学科。保存后，此集内的 {questionCount} 道题会随错题集归入所选学科。"); hint.Margin = new Thickness(0, 8, 0, 12); form.Children.Add(hint);
        form.Children.Add(new TextBlock { Text = "所属学科", Margin = new Thickness(0, 0, 0, 5) });
        subject.ItemsSource = new[] { LibraryCatalog.Unclassified }.Concat(config.Subjects).ToArray();
        subject.SelectedItem = string.IsNullOrWhiteSpace(initialSubject) ? LibraryCatalog.Unclassified : initialSubject; form.Children.Add(subject);
        form.Children.Add(new TextBlock { Text = "错题集名称", Margin = new Thickness(0, 12, 0, 5) }); name.Text = id is null ? "" : LibraryCatalog.Entry(config, id).Name; form.Children.Add(name);
        root.Children.Add(form); Content = root;
        ok.Click += (_, _) =>
        {
            if (subject.SelectedItem is not string) { MessageBox.Show(this, "请选择学科。"); return; }
            if (!LibraryCatalog.NameAvailable(config, CollectionName, Subject, id)) { MessageBox.Show(this, "请输入名称；同一学科内不能有同名错题集，也不能使用“全部错题”。"); return; }
            DialogResult = true;
        };
        Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
    }
}
