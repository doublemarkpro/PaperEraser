using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PaperEraser;
internal sealed record BackupData(IReadOnlyList<QuestionItem> Questions, LibraryConfig Library);
internal static class BackupService
{
    private const long MaxBasketBytes = 256L * 1024 * 1024;
    private const long MaxLibraryBytes = 4L * 1024 * 1024;
    private static void WriteEntry(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal); using var stream = entry.Open(); stream.Write(bytes);
    }
    internal static void Create(string destination, IReadOnlyList<QuestionItem> questions, LibraryConfig library)
    {
        var tempBasket = Path.Combine(Path.GetTempPath(), "papereraser-" + Guid.NewGuid().ToString("N") + ".json");
        var parent = Path.GetDirectoryName(Path.GetFullPath(destination))!;
        var tempArchive = Path.Combine(parent, ".papereraser-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            BasketStore.Save(tempBasket, questions);
            if (new FileInfo(tempBasket).Length > MaxBasketBytes) throw new InvalidDataException("错题数据超过备份格式允许的大小。");
            var libraryBytes = JsonSerializer.SerializeToUtf8Bytes(library);
            if (libraryBytes.Length > MaxLibraryBytes) throw new InvalidDataException("学科和错题集配置过大。");
            using (var file = new FileStream(tempArchive, FileMode.CreateNew, FileAccess.Write))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                WriteEntry(zip, "format.json", Encoding.UTF8.GetBytes("{\"format\":\"PaperEraserBackup\",\"version\":1}"));
                WriteEntry(zip, "basket.json", File.ReadAllBytes(tempBasket));
                WriteEntry(zip, "library.json", libraryBytes);
            }
            File.Move(tempArchive, destination, true);
        }
        finally { if (File.Exists(tempBasket)) File.Delete(tempBasket); if (File.Exists(tempArchive)) File.Delete(tempArchive); }
    }
    private static byte[] ReadEntry(ZipArchive zip, string name, long max)
    {
        var matches = zip.Entries.Where(e => e.FullName == name).ToArray();
        if (matches.Length != 1 || matches[0].Length > max) throw new InvalidDataException($"备份中的 {name} 缺失、重复或过大。");
        using var source = matches[0].Open(); using var output = new MemoryStream(); source.CopyTo(output);
        if (output.Length > max) throw new InvalidDataException("备份内容过大。");
        return output.ToArray();
    }
    internal static BackupData Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        using var format = JsonDocument.Parse(ReadEntry(zip, "format.json", 4096));
        if (format.RootElement.GetProperty("format").GetString() != "PaperEraserBackup" || format.RootElement.GetProperty("version").GetInt32() != 1) throw new InvalidDataException("不支持此备份格式。");
        var basket = ReadEntry(zip, "basket.json", MaxBasketBytes); var libraryBytes = ReadEntry(zip, "library.json", MaxLibraryBytes);
        var config = JsonSerializer.Deserialize<LibraryConfig>(libraryBytes) ?? throw new InvalidDataException("学科和错题集配置缺失。");
        if (config.Subjects is null || config.Collections is null || config.Subjects.Any(string.IsNullOrWhiteSpace) || config.Collections.Any(string.IsNullOrWhiteSpace) || config.Subjects.Distinct().Count() != config.Subjects.Count || config.Collections.Distinct().Count() != config.Collections.Count || config.Subjects.Contains("全部学科") || config.Collections.Contains("全部错题")) throw new InvalidDataException("学科或错题集配置无效。");
        var temp = Path.Combine(Path.GetTempPath(), "papereraser-restore-" + Guid.NewGuid().ToString("N") + ".json");
        try { File.WriteAllBytes(temp, basket); return new BackupData(BasketStore.Load(temp), config); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    internal static string Restore(BackupData data)
    {
        var root = Path.GetDirectoryName(BasketStore.DefaultPath)!; Directory.CreateDirectory(root);
        var archiveDir = Path.Combine(root, "restore-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(archiveDir);
        var targets = new[] { BasketStore.DefaultPath, LibraryStore.PathName };
        var existed = targets.Select(File.Exists).ToArray();
        for (var i = 0; i < targets.Length; i++) if (existed[i]) File.Copy(targets[i], Path.Combine(archiveDir, Path.GetFileName(targets[i])));
        try { BasketStore.Save(targets[0], data.Questions); LibraryStore.Save(data.Library); return archiveDir; }
        catch (Exception ex)
        {
            try
            {
                for (var i = 0; i < targets.Length; i++)
                {
                    var saved = Path.Combine(archiveDir, Path.GetFileName(targets[i]));
                    if (existed[i]) File.Copy(saved, targets[i], true); else if (File.Exists(targets[i])) File.Delete(targets[i]);
                }
            }
            catch (Exception rollback) { throw new IOException($"恢复失败，回滚也失败。原文件备份在 {archiveDir}；请勿关闭或覆盖备份。{rollback.Message}", ex); }
            throw new IOException($"恢复失败，原数据已回滚；原文件备份在 {archiveDir}。", ex);
        }
    }
}
