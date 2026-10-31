using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PaperEraser;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        static void Check(bool result, string name) { if (!result) throw new Exception(name); Console.WriteLine("PASS " + name); }
        var image = BitmapSource.Create(20, 20, 96, 96, PixelFormats.Bgr24, null, new byte[1200], 60); image.Freeze();
        var legacy = new LibraryConfig { Collections = new() { "默认错题集", "语文-同步第一单元", "空集" } };
        var questions = new[]
        {
            new QuestionItem("语文题", image, "语文", Collection: "默认错题集"),
            new QuestionItem("数学题", image, "数学", Collection: "默认错题集"),
            new QuestionItem("未分类题", image, Collection: "默认错题集")
        };
        var migrated = LibraryCatalog.Normalize(legacy, questions);
        var selection = new PaperSelection(questions, legacy, new[] { questions[0], questions[2] });
        Check(selection.Selected.Select(q => q.Index).SequenceEqual(new[] { 0, 2 }), "initial assembly matches current filter");
        selection.Questions[1].Included = true;
        Check(selection.Selected.Select(q => q.Index).SequenceEqual(new[] { 0, 2, 1 }), "cross-collection picks append without clearing earlier selection");
        var beforeShuffle = selection.Selected.ToArray(); selection.Shuffle(new Random(42));
        Check(selection.Selected.Count == 3 && selection.Selected.Distinct().Count() == 3 && !selection.Selected.SequenceEqual(beforeShuffle), "shuffle preserves every selected question and changes order");
        selection.Questions[1].Included = false;
        Check(selection.Selected.All(q => q.Index != 1) && !selection.Questions[1].Included, "removing a selected question also unchecks its source");
        selection.RestoreOrder();
        Check(selection.Selected.Select(q => q.Index).SequenceEqual(new[] { 0, 2 }) && questions[0].Label == "语文题", "restore order affects assembly only");
        foreach (var q in selection.Questions) q.Included = false;
        selection.Shuffle(); selection.RestoreOrder();
        Check(selection.Selected.Count == 0, "empty assembly can be shuffled or restored safely");
        var equalCopy = questions[0] with { };
        var duplicates = new PaperSelection(new[] { questions[0], equalCopy }, legacy, new[] { equalCopy });
        Check(duplicates.Selected.Count == 1 && duplicates.Selected[0].Index == 1, "equal-looking questions remain independently selectable");
        Check(migrated.Config.Version == 3 && migrated.Config.Collections.Count == 5, "mixed legacy collection is split by subject");
        Check(migrated.Questions.Length == 3 && migrated.Questions.Select(q => q.Label).SequenceEqual(questions.Select(q => q.Label)) && migrated.Questions.All(q => ReferenceEquals(q.Image, image)), "migration preserves images, order and question count");
        Check(migrated.Questions.Select(q => q.Collection).Distinct().Count() == 3 && migrated.Questions.All(q => LibraryCatalog.Entry(migrated.Config, q.Collection).Subject == q.Subject), "every migrated question matches its parent subject");
        Check(LibraryCatalog.Entry(migrated.Config, "语文-同步第一单元").Subject == "语文" && LibraryCatalog.Entry(migrated.Config, "空集").Subject == "", "empty collections use conservative prefix inference");
        var repeated = LibraryCatalog.Normalize(migrated.Config, migrated.Questions);
        Check(JsonSerializer.Serialize(repeated.Config) == JsonSerializer.Serialize(migrated.Config) && repeated.Questions.SequenceEqual(migrated.Questions), "migration is idempotent");
        var catalog = migrated.Config.Copy();
        catalog.Collections.AddRange(new[] { "chinese-unit", "math-unit" });
        catalog.CollectionDefinitions["chinese-unit"] = new("同步第一单元", "语文");
        catalog.CollectionDefinitions["math-unit"] = new("同步第一单元", "数学");
        LibraryCatalog.Validate(catalog);
        Check(!LibraryCatalog.NameAvailable(catalog, "同步第一单元", "语文") && LibraryCatalog.NameAvailable(catalog, "同步第一单元", "英语"), "same names are allowed across subjects but not within one subject");
        Check(catalog.Grades.Count == 12 && catalog.Grades.Contains("三年级") && catalog.Grades.Contains("高三"), "school grades are available without manual setup");
        catalog.Collections.AddRange(new[] { "grade-three", "grade-four" });
        catalog.CollectionDefinitions["grade-three"] = new("同步第一单元", "语文", "三年级");
        catalog.CollectionDefinitions["grade-four"] = new("同步第一单元", "语文", "四年级"); LibraryCatalog.Validate(catalog);
        Check(!LibraryCatalog.NameAvailable(catalog, "同步第一单元", "语文", grade: "三年级") && LibraryCatalog.NameAvailable(catalog, "同步第一单元", "语文", grade: "五年级"), "same unit names are isolated by grade and subject");
        Check(LibraryCatalog.Entry(catalog, "grade-three").Display == "三年级 / 语文 / 同步第一单元", "collection display includes full grade hierarchy");
        catalog.Collections.Add("grade-three-second");
        catalog.CollectionDefinitions["grade-three-second"] = new("同步第二单元", "语文", "三年级");
        var tree = LibraryTree.Build(catalog, "三年级", "语文", "三年级 · 语文 · 全部错题");
        Check(tree.Id is null && tree.Children.Count == 1 && tree.Children[0].Name == "三年级", "aggregate is the parent of the grade");
        var subjectNode = tree.Children[0].Children.Single();
        Check(subjectNode.Subject == "语文" && subjectNode.Children.Select(n => n.Id).SequenceEqual(new[] { "grade-three", "grade-three-second" }), "units are ordered children of their subject, never aggregate siblings");
        Check(tree.Descendants().Select(n => n.Key).Distinct().Count() == tree.Descendants().Count(), "all tree nodes have distinct selection keys");
        var allTree = LibraryTree.Build(catalog, null, null, "全部错题");
        Check(allTree.Descendants().Count(n => n.Id == "grade-three") == 1 && allTree.Children.Any(n => n.Grade == "四年级"), "all-grades tree keeps collections under distinct grades");
        Check(!LibraryCatalog.Entry(catalog, "grade-three").ToString().Contains("CollectionEntry"), "fallback display never leaks a technical type name");
        var legacyV2 = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(migrated.Config))!;
        legacyV2["Version"] = 2; legacyV2.AsObject().Remove("Grades");
        foreach (var entry in legacyV2["CollectionDefinitions"]!.AsObject()) entry.Value!.AsObject().Remove("Grade");
        var upgraded = LibraryCatalog.Normalize(JsonSerializer.Deserialize<LibraryConfig>(legacyV2.ToJsonString())!, migrated.Questions);
        Check(upgraded.Config.Version == 3 && upgraded.Questions.SequenceEqual(migrated.Questions) && upgraded.Config.CollectionDefinitions.Values.All(c => c.Grade == ""), "v2 subject catalogs upgrade with identities preserved and grade unset");
        var folder = Path.Combine(Path.GetTempPath(), "PaperEraserLibraryChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); BasketStore.TestDataDirectory = folder;
        try
        {
            BasketStore.Save(BasketStore.DefaultPath, questions); LibraryStore.Save(legacy);
            var recovery = BackupService.Restore(new BackupData(migrated.Questions, catalog));
            Check(File.Exists(Path.Combine(recovery, "basket.json")) && File.Exists(Path.Combine(recovery, "library.json")), "migration transaction keeps original data backup");
            var archive = Path.Combine(folder, "test.zip"); BackupService.Create(archive, BasketStore.Load(BasketStore.DefaultPath), LibraryStore.Load());
            var roundtrip = BackupService.Read(archive);
            Check(roundtrip.Questions.Count == 3 && roundtrip.Library.CollectionDefinitions["math-unit"].Subject == "数学" && roundtrip.Library.CollectionDefinitions["grade-three"].Grade == "三年级", "backup retains grade ownership and questions");
            var basketBefore = File.ReadAllBytes(BasketStore.DefaultPath); var configBefore = File.ReadAllBytes(LibraryStore.PathName);
            var invalid = catalog.Copy(); invalid.CollectionDefinitions["math-unit"] = new("同步第一单元", "不存在的学科");
            bool rejected = false;
            try { BackupService.Restore(new BackupData(Array.Empty<QuestionItem>(), invalid)); } catch (IOException) { rejected = true; }
            Check(rejected && File.ReadAllBytes(BasketStore.DefaultPath).SequenceEqual(basketBefore) && File.ReadAllBytes(LibraryStore.PathName).SequenceEqual(configBefore), "failed update rolls back both files");
        }
        finally { BasketStore.TestDataDirectory = null; Directory.Delete(folder, true); }
    }
}
