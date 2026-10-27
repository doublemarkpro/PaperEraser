using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace PaperEraser;

internal sealed record QuestionItem(string Label, BitmapSource Image, string Subject = "", string Topic = "", string ReviewStatus = "待复习", DateTime? NextReview = null, string Notes = "", string Collection = "默认错题集")
{
    // Only normalise old automatic prefixes for display; the saved and editable label is untouched.
    public string CardTitle
    {
        get
        {
            var label = Label ?? "";
            var legacy = System.Text.RegularExpressions.Regex.Match(label, @"^\d{1,3}\.\s+(.+)$");
            if (legacy.Success) return legacy.Groups[1].Value;
            if (System.Text.RegularExpressions.Regex.IsMatch(label, @"^错题\s+\d+$")) return "未命名错题";
            return label;
        }
    }
    public string Display => $"{(string.IsNullOrWhiteSpace(Subject) ? "未分类" : Subject)}  ·  {(string.IsNullOrWhiteSpace(Topic) ? "未设置知识点" : Topic)}  ·  {ReviewStatus}";
    public string DisplaySubject => string.IsNullOrWhiteSpace(Subject) ? "未分类" : Subject;
}
internal static class BasketStore
{
    private sealed record StoredQuestion(string Label, string ImageBase64, string? Subject = null, string? Topic = null, string? ReviewStatus = null, DateTime? NextReview = null, string? Notes = null, string? Collection = null);
    internal static string? TestDataDirectory { get; set; }
    internal static string DefaultPath => Path.Combine(TestDataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaperEraser"), "basket.json");
    internal static IReadOnlyList<QuestionItem> Load(string path)
    {
        if (!File.Exists(path)) return Array.Empty<QuestionItem>();
        var data = JsonSerializer.Deserialize<List<StoredQuestion>>(File.ReadAllText(path)) ?? throw new InvalidDataException("错题篮文件为空或格式不正确。");
        var items = new List<QuestionItem>();
        foreach (var entry in data)
        {
            using var stream = new MemoryStream(Convert.FromBase64String(entry.ImageBase64));
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
            items.Add(new QuestionItem(entry.Label, image, entry.Subject ?? "", entry.Topic ?? "", entry.ReviewStatus ?? "待复习", entry.NextReview, entry.Notes ?? "", string.IsNullOrWhiteSpace(entry.Collection) ? "默认错题集" : entry.Collection));
        }
        return items;
    }
    internal static void Save(string path, IEnumerable<QuestionItem> items)
    {
        var data = new List<StoredQuestion>();
        foreach (var item in items)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(item.Image));
            using var stream = new MemoryStream(); encoder.Save(stream);
            data.Add(new StoredQuestion(item.Label, Convert.ToBase64String(stream.ToArray()), item.Subject, item.Topic, item.ReviewStatus, item.NextReview, item.Notes, item.Collection));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(data)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
