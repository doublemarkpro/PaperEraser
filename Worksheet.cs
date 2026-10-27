using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Imaging;

namespace PaperEraser;
internal sealed class Worksheet : INotifyPropertyChanged
{
    internal Guid Id { get; }
    internal string Name { get; }
    internal byte[]? OriginalBytes { get; private set; }
    internal BitmapSource? Original { get; private set; }
    private BitmapSource? processed;
    internal BitmapSource? Processed { get => processed; set { processed = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail))); } }
    internal List<Int32Rect> Regions { get; } = new();
    public string Title => Name;
    public string State => Processed is not null ? $"已处理 · {Regions.Count} 个选区" : "待处理";
    public BitmapSource? Thumbnail => Processed ?? Original;
    internal void RegionsChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
    internal void Rotate(bool clockwise)
    {
        if (Original is not null)
        {
            Original = ImageOrientation.Rotate(Original, clockwise);
            OriginalBytes = ImageOrientation.EncodeForApi(Original);
        }
        if (processed is not null)
        {
            int oldWidth = processed.PixelWidth, oldHeight = processed.PixelHeight;
            processed = ImageOrientation.Rotate(processed, clockwise);
            for (int i = 0; i < Regions.Count; i++)
            {
                var r = Regions[i];
                Regions[i] = clockwise
                    ? new Int32Rect(oldHeight - r.Y - r.Height, r.X, r.Height, r.Width)
                    : new Int32Rect(r.Y, oldWidth - r.X - r.Width, r.Height, r.Width);
            }
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
    }
    internal Worksheet(string name, byte[]? bytes, BitmapSource? original, BitmapSource? processed = null, Guid? id = null) { Id = id ?? Guid.NewGuid(); Name = name; OriginalBytes = bytes; Original = original; this.processed = processed; }
    public event PropertyChangedEventHandler? PropertyChanged;
}
