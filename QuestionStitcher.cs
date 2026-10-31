using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PaperEraser;

internal sealed record QuestionFragment(Worksheet Page, int RegionIndex, Int32Rect Bounds, BitmapSource Source, BitmapSource Image)
{
    public string Label => $"{Page.Title} · 选区 {RegionIndex + 1}";
    internal static QuestionFragment Capture(Worksheet page, int index)
    {
        var source = page.Processed ?? throw new InvalidOperationException("页面还没有处理结果。");
        var bounds = page.Regions[index];
        var crop = new CroppedBitmap(source, bounds); crop.Freeze();
        return new(page, index, bounds, source, crop);
    }
    internal static bool AreCurrent(IReadOnlyList<QuestionFragment> fragments) => fragments.All(f =>
        ReferenceEquals(f.Page.Processed, f.Source) && f.RegionIndex >= 0 && f.RegionIndex < f.Page.Regions.Count && f.Page.Regions[f.RegionIndex] == f.Bounds);

    // Call only after the merged question has been saved successfully.
    internal static void Consume(IReadOnlyList<QuestionFragment> fragments)
    {
        if (!AreCurrent(fragments)) throw new InvalidOperationException("选区已发生变化，请重新选择片段。");
        foreach (var group in fragments.GroupBy(f => f.Page))
        {
            foreach (var index in group.Select(f => f.RegionIndex).Distinct().OrderDescending()) group.Key.Regions.RemoveAt(index);
            group.Key.RegionsChanged();
        }
    }
}

internal static class QuestionStitcher
{
    internal static BitmapSource Combine(IReadOnlyList<BitmapSource> images, int gap = 12, bool matchWidths = false)
    {
        if (images.Count < 2) throw new ArgumentException("请至少勾选两个题目片段。");
        if (gap < 0 || gap > 100) throw new ArgumentOutOfRangeException(nameof(gap));
        const int padding = 12;
        int contentWidth = Math.Min(4096, images.Max(i => i.PixelWidth));
        double commonScale = Math.Min(1, 4096.0 / images.Max(i => i.PixelWidth));
        var sizes = images.Select(image =>
        {
            double scale = matchWidths ? contentWidth / (double)image.PixelWidth : commonScale;
            return (Width: Math.Max(1, (int)Math.Round(image.PixelWidth * scale)), Height: Math.Max(1, (int)Math.Round(image.PixelHeight * scale)));
        }).ToArray();
        int width = contentWidth + 2 * padding;
        long height = sizes.Sum(s => (long)s.Height) + (long)gap * (images.Count - 1) + 2 * padding;
        if (height > 32767 || width * height > 32_000_000)
            throw new ArgumentException("拼接后的图片过大，请减少片段或缩小选区后再试。");
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            double top = padding;
            for (int i = 0; i < images.Count; i++)
            {
                drawing.DrawImage(images[i], new Rect(padding, top, sizes[i].Width, sizes[i].Height));
                top += sizes[i].Height + gap;
            }
        }
        var bitmap = new RenderTargetBitmap(width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
}
