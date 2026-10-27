using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace PaperEraser;
internal sealed class PhoneTransferWindow : Window
{
    private PhoneTransferServer? server;
    private string? saveFolder;
    private readonly TextBox links = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 54 };
    private readonly ComboBox addressChoice = new() { MinWidth = 250, Margin = new Thickness(0, 7, 0, 10), Height = 34 };
    private readonly Image qr = new() { Width = 176, Height = 176, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock qrHint = UiFactory.Hint("开始接收后，这里会显示二维码");
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100, Height = 12, Margin = new Thickness(0, 8, 0, 0), Foreground = UiFactory.Brush("#2469EB"), Background = UiFactory.Brush("#E8EFF8") };
    private readonly TextBlock progressText = new() { Text = "等待手机传图", Margin = new Thickness(0, 7, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = UiFactory.Hint("请选择文件夹，开始后将显示手机访问地址。 ");
    private readonly Button start = new() { Content = "选择文件夹并开始", Padding = new Thickness(14, 8, 14, 8) };
    private readonly Button copy = new() { Content = "复制地址", IsEnabled = false };
    private readonly Button openFolder = new() { Content = "打开传图文件夹", IsEnabled = false };
    internal PhoneTransferWindow()
    {
        UiFactory.Configure(this); Title = "手机传图 · 局域网"; Width = 650; Height = 800; MinWidth = 500; MinHeight = 450;
        UiFactory.Primary(start);
        var stack = new StackPanel { Margin = new Thickness(22) };
        stack.Children.Add(new TextBlock { Text = "手机传图", FontSize = 23, FontWeight = FontWeights.SemiBold });
        var hint = UiFactory.Hint("手机和电脑连接同一个可信 Wi-Fi；选好文件夹后，用手机扫码发送照片。关闭窗口即停止接收。"); hint.Margin = new Thickness(0, 6, 0, 15); stack.Children.Add(hint);
        var buttons = new WrapPanel(); buttons.Children.Add(start); buttons.Children.Add(copy); buttons.Children.Add(openFolder); stack.Children.Add(buttons);
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "选择网络地址", FontWeight = FontWeights.SemiBold }); body.Children.Add(addressChoice);
        var qrArea = new Grid { Height = 190, Background = UiFactory.Brush("#F4F7FC") }; qrHint.HorizontalAlignment = HorizontalAlignment.Center; qrHint.VerticalAlignment = VerticalAlignment.Center;
        qrArea.Children.Add(qrHint); qrArea.Children.Add(qr); body.Children.Add(new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = qrArea });
        body.Children.Add(new TextBlock { Text = "访问地址", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 15, 0, 6) }); body.Children.Add(links);
        body.Children.Add(progress); body.Children.Add(progressText); status.Margin = new Thickness(0, 12, 0, 0); body.Children.Add(status);
        var card = UiFactory.Card(body); card.Margin = new Thickness(0, 16, 0, 0); stack.Children.Add(card);
        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        start.Click += Start_Click;
        copy.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(links.Text)) Clipboard.SetText(links.Text); };
        openFolder.Click += (_, _) =>
        {
            if (saveFolder is null) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = saveFolder, UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "无法打开文件夹", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        addressChoice.SelectionChanged += (_, _) => UpdateAddress();
        Closed += async (_, _) => { if (server is not null) await server.DisposeAsync(); };
    }
    private void UpdateAddress()
    {
        if (server is null || addressChoice.SelectedItem is not string ip) return;
        var url = $"http://{ip}:{server.Port}/{server.Token}"; links.Text = url;
        using var generator = new QRCodeGenerator(); using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var code = new PngByteQRCode(data); var bytes = code.GetGraphic(5);
        using var stream = new MemoryStream(bytes); var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); qr.Source = image; qrHint.Visibility = Visibility.Collapsed;
    }
    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        var addresses = PhoneTransferServer.LocalAddresses();
        if (addresses.Count == 0) { MessageBox.Show("没有找到局域网 IPv4 地址。请先让电脑连接 Wi-Fi。", "无法开启"); return; }
        var dialog = new OpenFolderDialog { Title = "选择手机照片保存文件夹" }; if (dialog.ShowDialog() != true) return;
        saveFolder = dialog.FolderName; openFolder.IsEnabled = true;
        start.IsEnabled = false; var transfer = new PhoneTransferServer(saveFolder);
        transfer.Progress += (received, total, state) => Dispatcher.BeginInvoke(() =>
        {
            if (!IsVisible) return;
            progress.IsIndeterminate = !total.HasValue || total.Value <= 0;
            if (!progress.IsIndeterminate) progress.Value = Math.Clamp(100.0 * received / total!.Value, 0, 100);
            var percent = progress.IsIndeterminate ? "进度未知" : $"{progress.Value:0}%";
            progressText.Text = $"{state} · {percent} · 已接收 {received / 1048576.0:0.00} MB";
            if (state.StartsWith("保存完成") && progress.IsIndeterminate) { progress.IsIndeterminate = false; progress.Value = 100; }
        });
        try
        {
            await transfer.StartAsync();
            if (!IsVisible) { await transfer.DisposeAsync(); return; }
            server = transfer; addressChoice.ItemsSource = addresses; addressChoice.SelectedIndex = 0; copy.IsEnabled = true;
            status.Text = "接收中，保存至：" + saveFolder + "\n如无法连接，请检查 Wi-Fi 和 Windows 防火墙。地址含访问密钥，请勿分享。";
        }
        catch (Exception ex) { await transfer.DisposeAsync(); start.IsEnabled = true; MessageBox.Show(ex.Message, "无法开启手机传图", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
