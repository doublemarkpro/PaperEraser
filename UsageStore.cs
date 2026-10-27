using System.IO;
using System.Text.Json;

namespace PaperEraser;
internal static class UsageStore
{
    private sealed record Event(string Id, string State, DateTimeOffset At);
    internal static string PathName => Path.Combine(Path.GetDirectoryName(BasketStore.DefaultPath)!, "usage.jsonl");
    internal static void Record(string id, string state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        File.AppendAllText(PathName, JsonSerializer.Serialize(new Event(id, state, DateTimeOffset.Now)) + Environment.NewLine);
    }
    internal static (int Started, int Success, int Uncertain) GetCounts()
    {
        if (!File.Exists(PathName)) return (0, 0, 0);
        var statuses = new Dictionary<string, string>();
        foreach (var line in File.ReadLines(PathName))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var item = JsonSerializer.Deserialize<Event>(line) ?? throw new InvalidDataException("调用记录格式有误。");
            if (item.State == "started") { if (!statuses.ContainsKey(item.Id)) statuses.Add(item.Id, item.State); }
            else if (statuses.ContainsKey(item.Id)) statuses[item.Id] = item.State;
        }
        return (statuses.Count, statuses.Values.Count(s => s == "success"), statuses.Values.Count(s => s != "success"));
    }
}
