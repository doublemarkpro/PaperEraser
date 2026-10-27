using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PaperEraser;

internal sealed class PhoneTransferServer : IAsyncDisposable
{
    private const long MaxFile = 10 * 1024 * 1024;
    private readonly string folder;
    private readonly string token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
    private WebApplication? app;
    private readonly SemaphoreSlim uploadGate = new(1, 1);
    internal event Action<long, long?, string>? Progress;
    private void Notify(long received, long? total, string state) => Progress?.Invoke(received, total, state);
    internal int Port { get; private set; }
    internal string Token => token;
    internal PhoneTransferServer(string folder) => this.folder = folder;
    internal async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ApplicationName = typeof(PhoneTransferServer).Assembly.GetName().Name });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => { k.Listen(IPAddress.Any, 0); k.Limits.MaxRequestBodySize = 105 * 1024 * 1024; });
        builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => { o.MultipartBodyLengthLimit = 105 * 1024 * 1024; });
        var server = builder.Build();
        server.MapGet("/{key}", (string key) => key == token ? Results.Content(Html, "text/html; charset=utf-8") : Results.NotFound());
        server.MapPost("/{key}", async (HttpContext context, string key) =>
        {
            if (key != token) return Results.NotFound();
            if (!context.Request.HasFormContentType) return Results.BadRequest("需要图片表单");
            if (!await uploadGate.WaitAsync(0, context.RequestAborted)) return Results.Conflict("已有传输正在进行，请稍后再试。");
            long received = 0; var total = context.Request.ContentLength;
            try
            {
                Notify(0, total, "正在接收");
                long lastUpdate = 0;
                context.Request.Body = new CountingReadStream(context.Request.Body, count =>
                {
                    received = count;
                    var now = Environment.TickCount64;
                    if (now - lastUpdate >= 100 || (total.HasValue && received >= total.Value)) { lastUpdate = now; Notify(received, total, "正在接收"); }
                });
                var form = await context.Request.ReadFormAsync(context.RequestAborted);
                Notify(received, total, "接收完成，正在保存");
                if (form.Files.Count < 1 || form.Files.Count > 10) { Notify(received, total, "未保存：每次请选择 1 到 10 张图片"); return Results.BadRequest("每次请选择 1 到 10 张图片。"); }
                if (form.Files.Any(f => f.Length == 0 || f.Length > MaxFile)) { Notify(received, total, "未保存：图片为空或超过 10 MB"); return Results.BadRequest("每张图片需小于等于 10 MB，且不能为空。"); }
                var saved = 0; var rejected = 0;
                foreach (var file in form.Files)
                {
                    string? createdPath = null;
                    try
                    {
                        await using var input = file.OpenReadStream();
                        var header = new byte[12]; int count = await input.ReadAsync(header, context.RequestAborted);
                        var ext = ImageExtension(header, count);
                        if (ext is null) { rejected++; continue; }
                        var path = Path.Combine(folder, $"手机传图_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{ext}");
                        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                        createdPath = path;
                        await output.WriteAsync(header.AsMemory(0, count), context.RequestAborted);
                        await input.CopyToAsync(output, context.RequestAborted); saved++;
                    }
                    catch (Exception) { rejected++; if (createdPath is not null) { try { File.Delete(createdPath); } catch (IOException) { } } }
                }
                Notify(received, total, $"保存完成：成功 {saved} 张，失败 {rejected} 张");
                return Results.Content($"<!doctype html><html lang='zh-CN'><meta name='viewport' content='width=device-width,initial-scale=1'><body style='font-family:sans-serif;padding:24px'><h2>传图完成</h2><p>成功保存 {saved} 张，未保存 {rejected} 张。</p><p>支持 JPG、PNG、GIF、BMP、TIFF，每张不超过 10 MB。</p><a href='./{token}'>继续传图</a></body></html>", "text/html; charset=utf-8");
            }
            catch
            {
                Notify(received, total, "传输中断或失败，请查看手机提示后重试");
                throw;
            }
            finally { uploadGate.Release(); }
        }).DisableAntiforgery();
        try
        {
            await server.StartAsync();
            var address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            Port = new Uri(address.Replace("[::]", "127.0.0.1").Replace("0.0.0.0", "127.0.0.1")).Port;
            app = server;
        }
        catch { await server.DisposeAsync(); throw; }
    }
    internal static string? ImageExtension(byte[] b, int n)
    {
        if (n >= 3 && b[0] == 0xff && b[1] == 0xd8 && b[2] == 0xff) return ".jpg";
        if (n >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4e && b[3] == 0x47 && b[4] == 13 && b[5] == 10 && b[6] == 26 && b[7] == 10) return ".png";
        if (n >= 6 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46) return ".gif";
        if (n >= 2 && b[0] == 0x42 && b[1] == 0x4d) return ".bmp";
        if (n >= 4 && ((b[0] == 0x49 && b[1] == 0x49 && b[2] == 42 && b[3] == 0) || (b[0] == 0x4d && b[1] == 0x4d && b[2] == 0 && b[3] == 42))) return ".tif";
        return null;
    }
    internal static List<string> LocalAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address)
        .Where(a => a.AddressFamily == AddressFamily.InterNetwork && IsPrivate(a.GetAddressBytes()))
        .Select(a => a.ToString()).Distinct().ToList();
    private static bool IsPrivate(byte[] b) => b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
    public async ValueTask DisposeAsync() { if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); app = null; } }
    private const string Html = """
<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>PaperEraser 手机传图</title></head><body style="font-family:system-ui,sans-serif;padding:24px;line-height:1.6"><h2>PaperEraser 手机传图</h2><p>选择或拍摄试卷照片，发送到电脑。一次最多 10 张，每张不超过 10 MB。</p><p>支持 JPG、PNG、GIF、BMP、TIFF。传图不会调用夸克接口。</p><form method="post" enctype="multipart/form-data"><input type="file" name="images" accept="image/jpeg,image/png,image/gif,image/bmp,image/tiff" multiple required><p><button style="padding:12px 24px" type="submit">发送到电脑</button></p></form></body></html>
""";
}
