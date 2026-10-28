using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PaperEraser;

internal static class PdfExporter
{
    internal const double PageWidth = 595.28, PageHeight = 841.89;
    private const double PointsPerMm = 72.0 / 25.4;
    internal sealed record LayoutOptions(string Title = "", double MarginMm = 12.7, double GapMm = 5, double AnswerSpaceMm = 0, double ScalePercent = 100);
    internal sealed record Placement(BitmapSource Image, double X, double Y, double Width, double Height);
    private static BitmapSource TitleImage(string text)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, 1040, 110));
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight, new Typeface("Microsoft YaHei"), 46, Brushes.Black, 1.0)
            { MaxTextWidth = 1000, MaxTextHeight = 90, TextAlignment = TextAlignment.Center, Trimming = TextTrimming.CharacterEllipsis };
            drawing.DrawText(formatted, new System.Windows.Point(20, 10));
        }
        var bitmap = new RenderTargetBitmap(1040, 110, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    internal static List<List<Placement>> Layout(IReadOnlyList<BitmapSource> images, LayoutOptions? options = null)
    {
        if (images.Count == 0) throw new ArgumentException("没有可导出的图片。");
        options ??= new LayoutOptions();
        if (options.MarginMm < 8 || options.MarginMm > 25 || options.GapMm < 0 || options.GapMm > 30 || options.AnswerSpaceMm < 0 || options.AnswerSpaceMm > 50 || options.ScalePercent < 40 || options.ScalePercent > 100 || options.Title.Length > 50) throw new ArgumentException("排版参数超出允许范围。");
        var margin = options.MarginMm * PointsPerMm; var gap = options.GapMm * PointsPerMm; var blank = options.AnswerSpaceMm * PointsPerMm;
        var pages = new List<List<Placement>>(); var page = new List<Placement>(); pages.Add(page);
        double top = PageHeight - margin; var widthAvailable = PageWidth - 2 * margin;
        if (!string.IsNullOrWhiteSpace(options.Title))
        {
            var header = TitleImage(options.Title.Trim()); var headerHeight = widthAvailable * header.PixelHeight / header.PixelWidth;
            page.Add(new Placement(header, margin, top - headerHeight, widthAvailable, headerHeight)); top -= headerHeight + gap;
        }
        foreach (var image in images)
        {
            var scale = Math.Min(widthAvailable * options.ScalePercent / 100 / image.PixelWidth, (PageHeight - 2 * margin - blank) / image.PixelHeight);
            var width = image.PixelWidth * scale; var height = image.PixelHeight * scale;
            if (top - height - blank < margin && page.Count > 0)
            {
                if (pages.Count == 1 && page.Count == 1 && !string.IsNullOrWhiteSpace(options.Title))
                {
                    var available = top - margin - blank; scale = Math.Min(scale, available / image.PixelHeight); width = image.PixelWidth * scale; height = image.PixelHeight * scale;
                }
                else { page = new(); pages.Add(page); top = PageHeight - margin; }
            }
            var x = margin + (widthAvailable - width) / 2;
            page.Add(new Placement(image, x, top - height, width, height)); top -= height + blank + gap;
        }
        return pages;
    }
    internal static void Save(string path, IReadOnlyList<BitmapSource> images, LayoutOptions? options = null)
    {
        SaveLayout(path, Layout(images, options));
    }
    internal static void SavePages(string path, IReadOnlyList<BitmapSource> images)
    {
        if (images.Count == 0) throw new ArgumentException("没有可导出的已处理页面。");
        const double margin = 18;
        var pages = images.Select(image =>
        {
            var scale = Math.Min((PageWidth - 2 * margin) / image.PixelWidth, (PageHeight - 2 * margin) / image.PixelHeight);
            var width = image.PixelWidth * scale; var height = image.PixelHeight * scale;
            return new List<Placement> { new(image, (PageWidth - width) / 2, (PageHeight - height) / 2, width, height) };
        }).ToList();
        SaveLayout(path, pages);
    }
    private static void SaveLayout(string path, List<List<Placement>> pages)
    {
        var objects = new List<byte[]>();
        int Add(byte[] bytes) { objects.Add(bytes); return objects.Count; }
        byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
        string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        byte[] StreamObject(string dictionary, byte[] content)
        {
            using var stream = new MemoryStream();
            var header = Ascii($"<< {dictionary} /Length {content.Length} >>\nstream\n"); stream.Write(header); stream.Write(content); stream.Write(Ascii("\nendstream")); return stream.ToArray();
        }
        Add(Array.Empty<byte>()); Add(Array.Empty<byte>());
        var pageIds = new List<int>();
        foreach (var items in pages)
        {
            var imageRefs = new List<(string Name, int Id)>(); var commands = new StringBuilder();
            foreach (var item in items)
            {
                var converted = new FormatConvertedBitmap(item.Image, PixelFormats.Bgr24, null, 0);
                var encoder = new JpegBitmapEncoder { QualityLevel = 95 }; encoder.Frames.Add(BitmapFrame.Create(converted));
                using var buffer = new MemoryStream(); encoder.Save(buffer);
                var id = Add(StreamObject($"/Type /XObject /Subtype /Image /Width {item.Image.PixelWidth} /Height {item.Image.PixelHeight} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode", buffer.ToArray()));
                var name = "Im" + imageRefs.Count; imageRefs.Add((name, id));
                commands.Append($"q {Num(item.Width)} 0 0 {Num(item.Height)} {Num(item.X)} {Num(item.Y)} cm /{name} Do Q\n");
            }
            var contentId = Add(StreamObject("", Ascii(commands.ToString())));
            var refs = string.Join(" ", imageRefs.Select(i => $"/{i.Name} {i.Id} 0 R"));
            var pageId = Add(Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Num(PageWidth)} {Num(PageHeight)}] /Resources << /XObject << {refs} >> >> /Contents {contentId} 0 R >>"));
            pageIds.Add(pageId);
        }
        objects[0] = Ascii("<< /Type /Catalog /Pages 2 0 R >>");
        objects[1] = Ascii($"<< /Type /Pages /Count {pageIds.Count} /Kids [{string.Join(" ", pageIds.Select(id => $"{id} 0 R"))}] >>");
        using var output = File.Create(path);
        void Write(string s) => output.Write(Ascii(s));
        Write("%PDF-1.4\n% PaperEraser\n"); var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Count; i++) { offsets.Add(output.Position); Write($"{i + 1} 0 obj\n"); output.Write(objects[i]); Write("\nendobj\n"); }
        var xref = output.Position; Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }
}
