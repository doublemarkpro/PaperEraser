using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media.Imaging;

namespace PaperEraser;

internal sealed class PaperQuestion : INotifyPropertyChanged
{
    internal PaperQuestion(int index, QuestionItem question, string collection) { Index = index; Question = question; Collection = collection; }
    public int Index { get; }
    public QuestionItem Question { get; }
    public string Name => Question.Label;
    public BitmapSource Image => Question.Image;
    public string Collection { get; }
    private bool included;
    public bool Included { get => included; set { if (included == value) return; included = value; PropertyChanged?.Invoke(this, new(nameof(Included))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed class PaperSelection
{
    internal IReadOnlyList<PaperQuestion> Questions { get; }
    internal ObservableCollection<PaperQuestion> Selected { get; } = new();
    internal PaperSelection(IReadOnlyList<QuestionItem> questions, LibraryConfig library, IReadOnlyList<QuestionItem> initial)
    {
        var initialSet = initial.ToHashSet(ReferenceEqualityComparer.Instance);
        Questions = questions.Select((q, i) => new PaperQuestion(i, q, LibraryCatalog.Entry(library, q.Collection).Display)).ToArray();
        foreach (var item in Questions)
        {
            item.PropertyChanged += (_, _) => { if (item.Included) Selected.Add(item); else Selected.Remove(item); };
            item.Included = initialSet.Contains(item.Question);
        }
    }
    internal void Shuffle(Random? random = null)
    {
        random ??= Random.Shared;
        var previous = Selected.ToArray();
        for (int i = Selected.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (Selected[i], Selected[j]) = (Selected[j], Selected[i]);
        }
        if (Selected.Count > 1 && previous.SequenceEqual(Selected)) Selected.Move(0, Selected.Count - 1);
    }
    internal void RestoreOrder()
    {
        var original = Selected.OrderBy(q => q.Index).ToArray();
        for (int i = 0; i < original.Length; i++) Selected.Move(Selected.IndexOf(original[i]), i);
    }
    internal void MoveToGap(PaperQuestion item, int gap)
    {
        int from = Selected.IndexOf(item);
        if (from < 0 || gap < 0 || gap > Selected.Count) return;
        int destination = gap - (from < gap ? 1 : 0);
        if (destination != from) Selected.Move(from, destination);
    }
}
