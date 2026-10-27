using System.IO;
using System.Text.Json;

namespace PaperEraser;
internal sealed class LibraryConfig
{
    public List<string> Subjects { get; set; } = new() { "语文", "数学", "英语" };
    public List<string> Collections { get; set; } = new() { "默认错题集" };
}
internal static class LibraryStore
{
    internal static string PathName => Path.Combine(Path.GetDirectoryName(BasketStore.DefaultPath)!, "library.json");
    internal static LibraryConfig Load()
    {
        if (!File.Exists(PathName)) return new LibraryConfig();
        var config = JsonSerializer.Deserialize<LibraryConfig>(File.ReadAllText(PathName)) ?? throw new InvalidDataException("错题集配置为空。");
        if (config.Subjects is null || config.Collections is null) throw new InvalidDataException("错题集配置格式不正确。");
        return config;
    }
    internal static void Save(LibraryConfig config)
    {
        var path = PathName; Directory.CreateDirectory(Path.GetDirectoryName(path)!); var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(config)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
