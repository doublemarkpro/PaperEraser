using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace PaperEraser;
internal static class WorksheetStore
{
    private sealed record Region(int X, int Y, int Width, int Height);
    private sealed record Page(Guid Id, string Name, string? OriginalFile, string? ProcessedFile, List<Region> Regions);
    private sealed record Manifest(int Version, Guid? Selected, List<Page> Pages);
    private sealed record CachedFile(string Name);
    private static readonly ConditionalWeakTable<object, CachedFile> cachedFiles = new();
    internal static string DirectoryPath => Path.Combine(Path.GetDirectoryName(BasketStore.DefaultPath)!, "session");
    internal static string ManifestPath => Path.Combine(DirectoryPath, "session.json");
    internal static string Fingerprint() => File.Exists(ManifestPath) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ManifestPath))) : "(missing)";
    private static string StoreBytes(object key, byte[] bytes, string ext)
    {
        if (cachedFiles.TryGetValue(key, out var cached) && File.Exists(Path.Combine(DirectoryPath, cached.Name))) return cached.Name;
        var name = Convert.ToHexStringLower(SHA256.HashData(bytes)) + ext; var path = Path.Combine(DirectoryPath, name);
        if (!File.Exists(path))
        {
            var temp = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + ".tmp");
            try { File.WriteAllBytes(temp, bytes); try { File.Move(temp, path); } catch (IOException) when (File.Exists(path)) { } }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        cachedFiles.Remove(key); cachedFiles.Add(key, new CachedFile(name)); return name;
    }
    private static byte[] Encode(BitmapSource image)
    {
        using var stream = new MemoryStream(); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(stream); return stream.ToArray();
    }
    private static string StoreImage(BitmapSource image)
    {
        if (cachedFiles.TryGetValue(image, out var cached) && File.Exists(Path.Combine(DirectoryPath, cached.Name))) return cached.Name;
        return StoreBytes(image, Encode(image), ".png");
    }
    internal static void Save(IReadOnlyList<Worksheet> pages, Guid? selected)
    {
        Directory.CreateDirectory(DirectoryPath);
        var entries = pages.Select(page => new Page(page.Id, page.Name,
            page.OriginalBytes is null ? null : StoreBytes(page.OriginalBytes, page.OriginalBytes, ".bin"),
            page.Processed is null ? null : StoreImage(page.Processed),
            page.Regions.Select(r => new Region(r.X, r.Y, r.Width, r.Height)).ToList())).ToList();
        var temp = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllText(temp, JsonSerializer.Serialize(new Manifest(1, selected, entries))); File.Move(temp, ManifestPath, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static byte[] ReadFile(string name, string ext)
    {
        if (name.Length != 64 + ext.Length || !name.EndsWith(ext, StringComparison.Ordinal) || !name.AsSpan(0, 64).ToString().All(Uri.IsHexDigit)) throw new InvalidDataException("试卷缓存文件名不合法。");
        var bytes = File.ReadAllBytes(Path.Combine(DirectoryPath, name));
        if (!Convert.ToHexStringLower(SHA256.HashData(bytes)).Equals(name[..64], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("试卷缓存文件校验失败。");
        return bytes;
    }
    private static BitmapImage Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
    }
    internal static (List<Worksheet> Pages, Guid? Selected) Load()
    {
        if (!File.Exists(ManifestPath)) return (new List<Worksheet>(), null);
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(ManifestPath)) ?? throw new InvalidDataException("试卷进度文件为空。");
        if (manifest.Version != 1 || manifest.Pages is null || manifest.Pages.Count > 2000 || manifest.Pages.Select(p => p.Id).Distinct().Count() != manifest.Pages.Count) throw new InvalidDataException("试卷进度格式不正确。");
        var pages = new List<Worksheet>();
        foreach (var entry in manifest.Pages)
        {
            if (entry.Id == Guid.Empty || string.IsNullOrWhiteSpace(entry.Name) || entry.Regions is null) throw new InvalidDataException("试卷进度内容不正确。");
            var originalBytes = entry.OriginalFile is null ? null : ReadFile(entry.OriginalFile, ".bin");
            var original = originalBytes is null ? null : Decode(originalBytes);
            var processed = entry.ProcessedFile is null ? null : Decode(ReadFile(entry.ProcessedFile, ".png"));
            if (original is null && processed is null) throw new InvalidDataException("试卷图片缺失。");
            var page = new Worksheet(entry.Name, originalBytes, original, processed, entry.Id);
            foreach (var region in entry.Regions)
            {
                if (processed is null || region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0 || (long)region.X + region.Width > processed.PixelWidth || (long)region.Y + region.Height > processed.PixelHeight) throw new InvalidDataException("试卷选区无效。");
                page.Regions.Add(new Int32Rect(region.X, region.Y, region.Width, region.Height));
            }
            pages.Add(page);
        }
        return (pages, manifest.Selected);
    }
}
