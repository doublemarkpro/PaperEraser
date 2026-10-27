using System.Windows;
using System.Windows.Controls;

namespace PaperEraser;
internal sealed class NameDialog : Window
{
    private readonly TextBox input = new();
    internal string Value => input.Text.Trim();
    internal NameDialog(string title, string label, string initial = "")
    {
        UiFactory.Configure(this); Title = title; Width = 460; Height = 250; ResizeMode = ResizeMode.NoResize;
        var root = new DockPanel { Margin = new Thickness(22) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = "确定", IsDefault = true, Padding = new Thickness(20, 8, 20, 8) }; UiFactory.Primary(ok);
        var cancel = new Button { Content = "取消", IsCancel = true, Padding = new Thickness(20, 8, 20, 8), Margin = new Thickness(0) };
        ok.Click += (_, _) => { if (Value.Length == 0) { MessageBox.Show("请输入名称。"); return; } DialogResult = true; };
        buttons.Children.Add(ok); buttons.Children.Add(cancel); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 15) });
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 7) }); input.Text = initial; input.Height = 34; panel.Children.Add(input);
        root.Children.Add(panel); Content = root;
        Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
    }
}
