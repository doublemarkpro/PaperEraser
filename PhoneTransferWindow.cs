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
    private bool shuttingDown;
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
    private readonly Button stop = new() { Content = "停止接收", IsEnabled = false };
    internal PhoneTransferWindow()
    {
        UiFactory.Configure(this); Title = "手机传图 · 局域网"; Width = 650; Height = 800; MinWidth = 500; MinHeight = 450;
        UiFactory.Primary(start);
        var stack = new StackPanel { Margin = new Thickness(22) };
        stack.Children.Add(new TextBlock { Text = "手机传图", FontSize = 23, FontWeight = FontWeights.SemiBold });
        var hint = UiFactory.Hint("手机和电脑连接同一个可信 Wi-Fi，扫码一次即可连续拍照、传图。关闭此窗口只会隐藏，仍在后台接收；再点“手机传图”可找回原二维码。点击“停止接收”或退出软件才会断开。"); hint.Margin = new Thickness(0, 6, 0, 15); stack.Children.Add(hint);
        var buttons = new WrapPanel(); buttons.Children.Add(start); buttons.Children.Add(copy); buttons.Children.Add(openFolder); stack.Children.Add(buttons);
        var sessionActions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var hide = new Button { Content = "隐藏窗口，继续接收" }; sessionActions.Children.Add(hide); sessionActions.Children.Add(stop); stack.Children.Add(sessionActions);
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
        hide.Click += (_, _) => Hide();
        stop.Click += async (_, _) => await StopAsync();
        Closing += (_, e) => { if (!shuttingDown) { e.Cancel = true; Hide(); } };
        Closed += async (_, _) => { shuttingDown = true; await StopAsync(); };
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
        start.IsEnabled = false; var transfer = new PhoneTransferServer(saveFolder); server = transfer;
        transfer.Progress += (received, total, state) => Dispatcher.BeginInvoke(() =>
        {
            if (shuttingDown || !ReferenceEquals(server, transfer)) return;
            progress.IsIndeterminate = !total.HasValue || total.Value <= 0;
            if (!progress.IsIndeterminate) progress.Value = Math.Clamp(100.0 * received / total!.Value, 0, 100);
            var percent = progress.IsIndeterminate ? "进度未知" : $"{progress.Value:0}%";
            progressText.Text = $"{state} · {percent} · 已接收 {received / 1048576.0:0.00} MB";
            if (state.StartsWith("保存完成") && progress.IsIndeterminate) { progress.IsIndeterminate = false; progress.Value = 100; }
        });
        try
        {
            await transfer.StartAsync();
            if (shuttingDown || !ReferenceEquals(server, transfer)) { await transfer.DisposeAsync(); return; }
            addressChoice.ItemsSource = addresses; addressChoice.SelectedIndex = 0; copy.IsEnabled = true; stop.IsEnabled = true;
            status.Text = "持续接收中，保存至：" + saveFolder + "\n手机可连续拍照，页面无需关闭或重新扫码；隐藏本窗口后仍可接收。\n如无法连接，请检查 Wi-Fi 和 Windows 防火墙。地址含访问密钥，请勿分享。";
        }
        catch (Exception ex)
        {
            await transfer.DisposeAsync(); if (ReferenceEquals(server, transfer)) server = null;
            if (!shuttingDown) { start.IsEnabled = true; MessageBox.Show(this, ex.Message, "无法开启手机传图", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
    }
    private async Task StopAsync()
    {
        var transfer = server; server = null; stop.IsEnabled = false; copy.IsEnabled = false;
        links.Clear(); qr.Source = null; qrHint.Visibility = Visibility.Visible; qrHint.Text = "接收已停止，重新开始后将生成新二维码";
        addressChoice.ItemsSource = null; progress.IsIndeterminate = false; progress.Value = 0;
        progressText.Text = "已停止接收，手机当前链接已失效";
        status.Text = "可以重新开始接收。手机需要扫描新二维码；已保存的照片仍在原文件夹中。";
        try { if (transfer is not null) await transfer.DisposeAsync(); }
        catch (Exception ex) { if (!shuttingDown) status.Text = "停止接收时发生错误：" + ex.Message; }
        finally { start.IsEnabled = !shuttingDown; }
    }
    internal async Task ShutdownAsync()
    {
        shuttingDown = true;
        await StopAsync(); Close();
    }
}
