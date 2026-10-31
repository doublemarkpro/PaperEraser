using System.IO;
using System.Text.Json;

namespace PaperEraser;
internal sealed class LibraryConfig
{
    public int Version { get; set; } = 1;
    public List<string> Subjects { get; set; } = new() { "语文", "数学", "英语" };
    public List<string> Collections { get; set; } = new() { "默认错题集" };
    public Dictionary<string, CollectionDefinition> CollectionDefinitions { get; set; } = new();
    internal LibraryConfig Copy() => new() { Version = Version, Subjects = Subjects.ToList(), Collections = Collections.ToList(), CollectionDefinitions = new(CollectionDefinitions) };
}
internal sealed record CollectionDefinition(string Name, string Subject);
internal sealed record CollectionEntry(string Id, string Name, string Subject)
{
    public string GroupName => Id == LibraryCatalog.AllCollections ? "总览" : string.IsNullOrWhiteSpace(Subject) ? LibraryCatalog.Unclassified : Subject;
    public string ColorKey => string.IsNullOrWhiteSpace(Subject) ? Name : Subject;
    public string Display => Id == LibraryCatalog.AllCollections ? Name : $"{GroupName} / {Name}";
}
internal static class LibraryCatalog
{
    internal const string AllCollections = "全部错题", AllSubjects = "全部学科", Unclassified = "未分类";
    internal static string SubjectKey(string? subject) => string.IsNullOrWhiteSpace(subject) || subject == Unclassified ? "" : subject.Trim();
    internal static CollectionEntry Entry(LibraryConfig config, string id)
    {
        var definition = config.CollectionDefinitions.GetValueOrDefault(id);
        return new(id, definition?.Name ?? id, definition?.Subject ?? "");
    }
    internal static IEnumerable<CollectionEntry> Entries(LibraryConfig config) => config.Collections.Select(id => Entry(config, id));
    internal static bool NameAvailable(LibraryConfig config, string name, string subject, string? except = null) =>
        !string.IsNullOrWhiteSpace(name) && name != AllCollections && !Entries(config).Any(c => c.Id != except && c.Name == name && c.Subject == SubjectKey(subject));
    internal static string NewId() => "collection-" + Guid.NewGuid().ToString("N");

    internal static (LibraryConfig Config, QuestionItem[] Questions) Normalize(LibraryConfig original, IReadOnlyList<QuestionItem> questions)
    {
        var config = original.Copy();
        config.Subjects = config.Subjects.Where(s => !string.IsNullOrWhiteSpace(s) && s != AllSubjects && s != Unclassified).Distinct().ToList();
        var updated = questions.ToArray();
        foreach (var subject in updated.Select(q => SubjectKey(q.Subject)).Where(s => s.Length > 0).Distinct()) if (!config.Subjects.Contains(subject)) config.Subjects.Add(subject);
        foreach (var key in updated.Select(q => q.Collection).Distinct()) if (!config.Collections.Contains(key)) config.Collections.Add(key);
        if (config.Collections.Count == 0) config.Collections.Add("默认错题集");
        var keys = config.Collections.ToArray();
        foreach (var key in keys)
        {
            if (config.CollectionDefinitions.ContainsKey(key)) continue;
            var members = updated.Where(q => q.Collection == key).ToArray();
            var subjects = members.Select(q => SubjectKey(q.Subject)).Distinct().ToArray();
            if (subjects.Length == 0)
            {
                var inferred = config.Subjects.OrderByDescending(s => s.Length).FirstOrDefault(s => key == s || new[] { "-", "—", "_", "·", "/", "：", ":" }.Any(separator => key.StartsWith(s + separator, StringComparison.Ordinal)));
                subjects = new[] { inferred ?? "" };
            }
            int position = config.Collections.IndexOf(key);
            for (int i = 0; i < subjects.Length; i++)
            {
                string id = i == 0 ? key : NewId();
                if (i > 0) config.Collections.Insert(position + i, id);
                config.CollectionDefinitions[id] = new CollectionDefinition(key, subjects[i]);
                for (int j = 0; j < updated.Length; j++)
                    if (updated[j].Collection == key && SubjectKey(updated[j].Subject) == subjects[i]) updated[j] = updated[j] with { Collection = id, Subject = subjects[i] };
            }
        }
        for (int i = 0; i < updated.Length; i++)
        {
            var owner = Entry(config, updated[i].Collection).Subject;
            if (updated[i].Subject != owner) updated[i] = updated[i] with { Subject = owner };
        }
        config.Version = 2; Validate(config);
        return (config, updated);
    }
    internal static void Validate(LibraryConfig config)
    {
        if (config.Version is < 1 or > 2 || config.Subjects is null || config.Collections is null || config.CollectionDefinitions is null ||
            config.Subjects.Any(string.IsNullOrWhiteSpace) || config.Collections.Any(string.IsNullOrWhiteSpace) || config.Subjects.Distinct().Count() != config.Subjects.Count || config.Collections.Distinct().Count() != config.Collections.Count ||
            config.Subjects.Contains(AllSubjects) || config.Collections.Contains(AllCollections)) throw new InvalidDataException("学科或错题集配置无效。");
        foreach (var (id, definition) in config.CollectionDefinitions)
            if (!config.Collections.Contains(id) || definition is null || string.IsNullOrWhiteSpace(definition.Name) || definition.Name == AllCollections || definition.Subject is null || (definition.Subject.Length > 0 && !config.Subjects.Contains(definition.Subject))) throw new InvalidDataException("错题集学科归属无效。");
        if (config.Version >= 2 && (config.Collections.Any(id => !config.CollectionDefinitions.ContainsKey(id)) || Entries(config).GroupBy(c => (c.Subject, c.Name)).Any(g => g.Count() > 1))) throw new InvalidDataException("同一学科的错题集名称不能重复。");
    }
}
internal static class LibraryStore
{
    internal static string PathName => Path.Combine(Path.GetDirectoryName(BasketStore.DefaultPath)!, "library.json");
    internal static LibraryConfig Load()
    {
        if (!File.Exists(PathName)) return new LibraryConfig();
        var config = JsonSerializer.Deserialize<LibraryConfig>(File.ReadAllText(PathName)) ?? throw new InvalidDataException("错题集配置为空。");
        LibraryCatalog.Validate(config);
        return config;
    }
    internal static void Save(LibraryConfig config)
    {
        LibraryCatalog.Validate(config);
        var path = PathName; Directory.CreateDirectory(Path.GetDirectoryName(path)!); var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(config)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
