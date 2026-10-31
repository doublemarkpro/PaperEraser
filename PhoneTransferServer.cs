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
    private sealed record UploadResult(int Saved, int Rejected, string[] Files);
    private readonly Dictionary<Guid, UploadResult> completedUploads = new();
    private readonly Queue<Guid> completedOrder = new();
    private int savedCount;
    internal int SavedCount => Volatile.Read(ref savedCount);
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
        server.MapGet("/{key}/status", (HttpContext context, string key) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return key == token ? Results.Json(new { saved = SavedCount }) : Results.NotFound();
        });
        server.MapPost("/{key}", async (HttpContext context, string key) =>
        {
            if (key != token) return Results.NotFound();
            if (!context.Request.HasFormContentType) return Results.BadRequest("需要图片表单");
            if (!await uploadGate.WaitAsync(0, context.RequestAborted)) return Results.Conflict("已有传输正在进行，请稍后再试。");
            long received = 0; var total = context.Request.ContentLength;
            try
            {
                context.Response.Headers.CacheControl = "no-store";
                bool wantsJson = context.Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
                var uploadIdText = context.Request.Headers["X-Upload-Id"].ToString();
                Guid? uploadId = Guid.TryParseExact(uploadIdText, "N", out var id) ? id : null;
                if (uploadIdText.Length > 0 && uploadId is null) return Results.BadRequest("上传编号不合法，请刷新手机页面。");
                if (uploadId.HasValue && completedUploads.TryGetValue(uploadId.Value, out var completed)) return Results.Json(completed);
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
                if (uploadId.HasValue && form.Files.Count != 1) return Results.BadRequest("连续传图每个上传编号只能对应一张图片。");
                var saved = 0; var rejected = 0;
                var savedFiles = new List<string>();
                foreach (var file in form.Files)
                {
                    string? createdPath = null;
                    try
                    {
                        await using var input = file.OpenReadStream();
                        var header = new byte[12]; int count = await input.ReadAtLeastAsync(header, header.Length, false, context.RequestAborted);
                        var ext = ImageExtension(header, count);
                        if (ext is null) { rejected++; continue; }
                        var path = Path.Combine(folder, $"手机传图_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{ext}");
                        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            createdPath = path;
                            await output.WriteAsync(header.AsMemory(0, count), context.RequestAborted);
                            await input.CopyToAsync(output, context.RequestAborted);
                            await output.FlushAsync(context.RequestAborted);
                        }
                        saved++; savedFiles.Add(Path.GetFileName(path));
                    }
                    catch (Exception) { rejected++; if (createdPath is not null) { try { File.Delete(createdPath); } catch (IOException) { } } }
                }
                Interlocked.Add(ref savedCount, saved);
                var result = new UploadResult(saved, rejected, savedFiles.ToArray());
                // Keep confirmed successes for this session so a retry after a lost response
                // does not save the same photo twice. Failed uploads may be tried again.
                if (uploadId.HasValue && saved > 0)
                {
                    completedUploads[uploadId.Value] = result; completedOrder.Enqueue(uploadId.Value);
                    if (completedOrder.Count > 10000) completedUploads.Remove(completedOrder.Dequeue());
                }
                Notify(received, total, $"保存完成：成功 {saved} 张，失败 {rejected} 张；本次累计 {SavedCount} 张");
                if (wantsJson) return Results.Json(result);
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
    public async ValueTask DisposeAsync()
    {
        var current = Interlocked.Exchange(ref app, null);
        if (current is null) return;
        try { await current.StopAsync(); }
        finally { await current.DisposeAsync(); }
    }
    private const string Html = """
<!doctype html>
<html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>PaperEraser 连续传图</title>
<style>
*{box-sizing:border-box}body{margin:0;background:#f4f7fc;color:#172746;font:15px/1.6 system-ui,sans-serif}main{max-width:640px;margin:auto;padding:24px 18px 40px}h1{font-size:27px;margin:0 0 5px}p{margin:6px 0 16px;color:#647994}.card{background:white;border:1px solid #dde7f3;border-radius:14px;padding:18px;margin:16px 0}.buttons{display:flex;gap:10px;flex-wrap:wrap}button{border:1px solid #cfdef1;border-radius:9px;background:#edf4ff;color:#245cc2;padding:12px 16px;font:inherit;cursor:pointer}button.primary{background:#2469eb;color:white;border-color:#2469eb}button:disabled{opacity:.5;cursor:default}.buttons>button{flex:1}.connection{font-size:13px;margin:12px 0;color:#58718f}.connection.offline{color:#b34b28}label{display:block;margin:14px 0}input[type=checkbox]{width:18px;height:18px;vertical-align:middle}#summary{font-weight:600;margin:14px 0 8px}#notice{min-height:24px;color:#a84c29;font-size:13px;white-space:pre-line}ul{padding:0;margin:0;list-style:none}li{display:flex;gap:12px;padding:12px 0;border-top:1px solid #e9eff6;align-items:center}.thumb{width:50px;height:65px;object-fit:contain;background:#eef3fa;border-radius:5px}.detail{flex:1;min-width:0}.filename{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:13px}.state{font-size:13px;color:#7388a2}.saved{color:#278152}.failed,.uncertain{color:#b34b28}progress{width:100%;height:7px}li button{padding:6px 10px;font-size:13px}.small{font-size:12px}.footer{font-size:13px;margin-top:20px}#clear{padding:5px 10px;font-size:12px;background:white}
</style></head><body><main>
<h1>连续拍照，传到电脑</h1><p>扫一次码，保持本页打开，就可以边拍边传。</p>
<div id="connection" class="connection" role="status">正在检查电脑连接…</div>
<section class="card">
<div class="buttons"><button id="cameraButton" class="primary" type="button">拍一张</button><button id="albumButton" type="button">选择照片</button></div>
<input id="camera" type="file" accept="image/jpeg,image/png,image/gif,image/bmp,image/tiff" capture="environment" hidden>
<input id="album" type="file" accept="image/jpeg,image/png,image/gif,image/bmp,image/tiff" multiple hidden>
<label><input id="automatic" type="checkbox" checked> 拍照 / 选照片后自动发送</label>
<div class="buttons"><button id="send" type="button">发送待传照片</button><button id="retryAll" type="button">重试未完成照片</button></div>
<div id="notice" role="status" aria-live="polite"></div>
</section>
<section class="card"><div id="summary" aria-live="polite">还没有添加照片</div><button id="clear" type="button">清理已完成记录</button><ul id="queue"></ul></section>
<p class="footer">每张不超过 10 MB，支持 JPG、PNG、GIF、BMP、TIFF。照片按顺序逐张发送；手机浏览器是否直接打开相机取决于系统。电脑可隐藏传图窗口继续整理，点击“停止接收”或退出软件后，当前链接失效。传图不会调用夸克 API。</p>
<noscript><form method="post" enctype="multipart/form-data"><p>当前浏览器未启用 JavaScript，可使用普通上传（一次最多 10 张）：</p><input type="file" name="images" accept="image/jpeg,image/png,image/gif,image/bmp,image/tiff" multiple required><button type="submit">发送到电脑</button></form></noscript>
</main><script>
(() => {
  'use strict';
  const $ = id => document.getElementById(id);
  const endpoint = location.pathname.replace(/\/$/, '');
  const queue = []; let active = false, pauseForConnection = false, totalSaved = 0, checking = false;
  const labels = {queued:'等待发送', uploading:'正在发送', saved:'电脑已保存', failed:'未保存，请重试', uncertain:'未收到确认，请重试核对'};
  function identifier() {
    const bytes = new Uint8Array(16);
    if (window.crypto && window.crypto.getRandomValues) window.crypto.getRandomValues(bytes);
    else for (let i=0; i<bytes.length; i++) bytes[i] = Math.floor(Math.random()*256);
    return Array.from(bytes, b => b.toString(16).padStart(2,'0')).join('');
  }
  function update() {
    const pending = queue.filter(i => i.state === 'queued').length;
    const failed = queue.filter(i => i.state === 'failed' || i.state === 'uncertain').length;
    const saved = queue.filter(i => i.state === 'saved').length;
    $('summary').textContent = queue.length ? `本页已保存 ${totalSaved} 张 · 待传 ${pending} 张 · 未完成 ${failed} 张${active ? ' · 正在发送' : ''}` : `本页已保存 ${totalSaved} 张，可继续拍照`;
    $('send').disabled = !pending || active;
    $('retryAll').disabled = !failed || active;
    $('clear').disabled = !saved;
  }
  function showState(item) {
    item.status.textContent = item.message || labels[item.state]; item.status.className = 'state ' + item.state;
    item.retry.hidden = item.state !== 'failed' && item.state !== 'uncertain';
    item.retry.disabled = active;
    item.progress.hidden = item.state !== 'uploading';
    update();
  }
  function add(files) {
    const errors = [];
    for (const file of files) {
      if (!file.size || file.size > 10*1024*1024) { errors.push(file.name + '：为空或超过 10 MB'); continue; }
      if (queue.length >= 500) { errors.push('本页记录较多，请清理已完成记录后继续添加。'); break; }
      const item = {id:identifier(), file, state:'queued', message:'', url:URL.createObjectURL(file)};
      const row = document.createElement('li'); item.row = row;
      const thumb = document.createElement('img'); thumb.className = 'thumb'; thumb.src = item.url; thumb.alt = '照片缩略图'; item.thumb = thumb; row.appendChild(thumb);
      const detail = document.createElement('div'); detail.className = 'detail';
      const name = document.createElement('div'); name.className = 'filename'; name.textContent = file.name; detail.appendChild(name);
      item.status = document.createElement('div'); detail.appendChild(item.status);
      item.progress = document.createElement('progress'); item.progress.max = 100; item.progress.value = 0; detail.appendChild(item.progress); row.appendChild(detail);
      item.retry = document.createElement('button'); item.retry.type='button'; item.retry.textContent='重试';
      item.retry.onclick = () => { if(active) return; item.state='queued'; item.message=''; pauseForConnection=false; showState(item); pump(true); };
      row.appendChild(item.retry); queue.push(item); $('queue').appendChild(row); showState(item);
    }
    $('notice').textContent = errors.join('\n'); update(); pump(false);
  }
  function upload(item) {
    return new Promise(resolve => {
      const xhr = new XMLHttpRequest(); xhr.open('POST', endpoint); xhr.timeout = 180000;
      xhr.setRequestHeader('Accept','application/json'); xhr.setRequestHeader('X-Upload-Id',item.id);
      xhr.upload.onprogress = e => { if(e.lengthComputable) item.progress.value = Math.round(100*e.loaded/e.total); };
      xhr.onload = () => {
        let result; try { result = JSON.parse(xhr.responseText); } catch (_) {}
        if(xhr.status >= 200 && xhr.status < 300 && result && result.saved === 1) {
          item.state='saved'; totalSaved++; item.message='电脑已保存，可继续拍下一张';
          URL.revokeObjectURL(item.url); item.url=null; item.file=null;
        } else if(xhr.status === 409) {
          item.state='failed'; item.message='电脑正在接收另一批照片，请稍后重试'; pauseForConnection=true;
        } else {
          item.state='failed'; item.message=result && result.rejected ? '图片未保存，请确认格式后重试' : '电脑未接受上传（HTTP '+xhr.status+'），请检查连接或图片大小';
          if(xhr.status >= 500 || xhr.status === 404 || xhr.status === 0) pauseForConnection=true;
        }
        resolve();
      };
      const uncertain = () => { item.state='uncertain'; item.message='连接中断，保存状态未确认。恢复连接后重试会用原编号核对，避免重复保存'; pauseForConnection=true; resolve(); };
      xhr.onerror=uncertain; xhr.ontimeout=uncertain; xhr.onabort=uncertain;
      const form=new FormData(); form.append('images',item.file,item.file.name);
      try { xhr.send(form); } catch (_) { uncertain(); }
    });
  }
  async function pump(force) {
    if(active || pauseForConnection || (!force && !$('automatic').checked)) return;
    active=true;
    try {
      let next;
      while(!pauseForConnection && (force || $('automatic').checked) && (next=queue.find(i=>i.state==='queued'))) {
        next.state='uploading'; next.message=''; next.progress.value=0; showState(next);
        await upload(next); showState(next);
      }
    } finally { active=false; queue.forEach(showState); update(); checkConnection(); }
  }
  async function checkConnection() {
    if(checking) return; checking=true;
    const controller=new AbortController(); const timer=setTimeout(()=>controller.abort(),5000);
    try {
      const response=await fetch(endpoint+'/status',{cache:'no-store',signal:controller.signal});
      if(!response.ok) throw new Error('offline'); const result=await response.json();
      $('connection').textContent='电脑连接正常 · 本次接收已保存 '+result.saved+' 张'; $('connection').className='connection';
      if(pauseForConnection) $('notice').textContent='连接已恢复。点击“重试未完成照片”或“发送待传照片”继续，无需重新扫码。';
    } catch (_) { $('connection').textContent='暂时连不上电脑，请保持同一 Wi-Fi 并确认电脑仍在接收；照片会留在当前页面等待手动重试。'; $('connection').className='connection offline'; }
    finally { clearTimeout(timer); checking=false; }
  }
  $('cameraButton').onclick=()=>$('camera').click(); $('albumButton').onclick=()=>$('album').click();
  for(const id of ['camera','album']) $(id).onchange=()=>{ add(Array.from($(id).files)); $(id).value=''; };
  $('automatic').onchange=()=>{ if($('automatic').checked) pump(false); };
  $('send').onclick=()=>{ pauseForConnection=false; pump(true); };
  $('retryAll').onclick=()=>{ queue.forEach(i=>{ if(i.state==='failed'||i.state==='uncertain'){ i.state='queued'; i.message=''; showState(i); } }); pauseForConnection=false; pump(true); };
  $('clear').onclick=()=>{ for(let i=queue.length-1;i>=0;i--) if(queue[i].state==='saved'){ queue[i].row.remove(); queue.splice(i,1); } update(); };
  window.addEventListener('beforeunload',e=>{ if(queue.some(i=>i.file)){ e.preventDefault(); e.returnValue=''; } });
  document.addEventListener('visibilitychange',()=>{ if(!document.hidden) checkConnection(); });
  update(); checkConnection(); setInterval(()=>{ if(!document.hidden) checkConnection(); },10000);
})();
</script></body></html>
""";
}
