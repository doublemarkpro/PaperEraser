using System.ComponentModel;
using System.Text.Json;

namespace PaperEraser;

internal sealed class CollectionTreeNode : INotifyPropertyChanged
{
    public string Name { get; init; } = "";
    public string Display { get; init; } = "";
    public string? Grade { get; init; }
    public string? Subject { get; init; }
    public string? Id { get; init; }
    public string ColorKey => Subject ?? Name;
    public string Key => JsonSerializer.Serialize(new[] { Grade, Subject, Id, Name });
    public List<CollectionTreeNode> Children { get; } = new();
    private bool expanded = true, selected;
    public bool IsExpanded { get => expanded; set { expanded = value; PropertyChanged?.Invoke(this, new(nameof(IsExpanded))); } }
    public bool IsSelected { get => selected; set { selected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    public IEnumerable<CollectionTreeNode> Descendants() => new[] { this }.Concat(Children.SelectMany(c => c.Descendants()));
}

internal static class LibraryTree
{
    internal static CollectionTreeNode Build(LibraryConfig config, string? grade, string? subject, string title)
    {
        var root = new CollectionTreeNode { Name = LibraryCatalog.AllCollections, Display = title, Grade = grade, Subject = subject };
        var entries = LibraryCatalog.Entries(config).Where(c => (grade is null || c.Grade == grade) && (subject is null || c.Subject == subject)).ToArray();
        var grades = grade is not null ? new[] { grade } : config.Grades.Append("").Where(g => entries.Any(c => c.Grade == g));
        foreach (var g in grades)
        {
            var gradeName = g.Length == 0 ? LibraryCatalog.UnsetGrade : g;
            var gradeNode = new CollectionTreeNode { Name = gradeName, Display = gradeName + " · 全部错题", Grade = g, Subject = subject };
            root.Children.Add(gradeNode);
            var subjects = subject is not null ? new[] { subject } : config.Subjects.Append("").Where(s => entries.Any(c => c.Grade == g && c.Subject == s));
            foreach (var s in subjects)
            {
                var subjectName = s.Length == 0 ? LibraryCatalog.Unclassified : s;
                var subjectNode = new CollectionTreeNode { Name = subjectName + " · 全部错题", Display = gradeName + " / " + subjectName + " · 全部错题", Grade = g, Subject = s };
                gradeNode.Children.Add(subjectNode);
                foreach (var c in entries.Where(c => c.Grade == g && c.Subject == s))
                    subjectNode.Children.Add(new() { Name = c.Name, Display = c.Display, Grade = g, Subject = s, Id = c.Id });
            }
        }
        return root;
    }
}
