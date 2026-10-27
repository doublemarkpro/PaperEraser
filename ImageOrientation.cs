using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PaperEraser;

internal static class ImageOrientation
{
    private static readonly string[] OrientationQueries = ["/app1/ifd/{ushort=274}", "/ifd/{ushort=274}"];

    internal static (BitmapSource Image, byte[] Bytes, bool Corrected) DecodeAndNormalize(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var orientation = ReadExifOrientation(frame.Metadata as BitmapMetadata);
        if (orientation == 1) { frame.Freeze(); return (frame, bytes, false); }
        var corrected = Apply(frame, orientation);
        return (corrected, EncodeForApi(corrected), true);
    }

    internal static BitmapSource Rotate(BitmapSource source, bool clockwise) => Apply(source, clockwise ? 6 : 8);

    internal static byte[] EncodeForApi(BitmapSource source)
    {
        byte[] encoded = [];
        foreach (var quality in new[] { 94, 88, 80, 70 })
        {
            using var stream = new MemoryStream();
            var encoder = new JpegBitmapEncoder { QualityLevel = quality };
            encoder.Frames.Add(BitmapFrame.Create(source)); encoder.Save(stream);
            encoded = stream.ToArray();
            if (encoded.Length <= 10 * 1024 * 1024) break;
        }
        return encoded;
    }

    private static ushort ReadExifOrientation(BitmapMetadata? metadata)
    {
        if (metadata is null) return 1;
        foreach (var query in OrientationQueries)
        {
            try
            {
                var value = metadata.GetQuery(query);
                var orientation = value switch
                {
                    ushort number when number is >= 1 and <= 8 => number,
                    uint number when number is >= 1 and <= 8 => (ushort)number,
                    _ => (ushort)1
                };
                if (orientation != 1) return orientation;
            }
            catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) { }
        }
        return 1;
    }

    // Apply all eight EXIF orientations explicitly, including mirrored camera images.
    private static BitmapSource Apply(BitmapSource source, int orientation)
    {
        // The common camera cases can stay in WPF's native imaging pipeline and avoid
        // allocating two full-size pixel buffers for a high-resolution phone photo.
        if (orientation is 3 or 6 or 8)
        {
            var angle = orientation == 3 ? 180 : orientation == 6 ? 90 : 270;
            var transformed = new TransformedBitmap(source, new RotateTransform(angle));
            transformed.Freeze(); return transformed;
        }
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int sourceWidth = converted.PixelWidth, sourceHeight = converted.PixelHeight;
        int targetWidth = orientation >= 5 ? sourceHeight : sourceWidth;
        int targetHeight = orientation >= 5 ? sourceWidth : sourceHeight;
        int sourceStride = sourceWidth * 4, targetStride = targetWidth * 4;
        var input = new byte[sourceStride * sourceHeight];
        var output = new byte[targetStride * targetHeight];
        converted.CopyPixels(input, sourceStride, 0);

        for (int y = 0; y < sourceHeight; y++)
        for (int x = 0; x < sourceWidth; x++)
        {
            var (targetX, targetY) = orientation switch
            {
                2 => (sourceWidth - 1 - x, y),
                3 => (sourceWidth - 1 - x, sourceHeight - 1 - y),
                4 => (x, sourceHeight - 1 - y),
                5 => (y, x),
                6 => (sourceHeight - 1 - y, x),
                7 => (sourceHeight - 1 - y, sourceWidth - 1 - x),
                8 => (y, sourceWidth - 1 - x),
                _ => (x, y)
            };
            Buffer.BlockCopy(input, y * sourceStride + x * 4, output, targetY * targetStride + targetX * 4, 4);
        }

        var bitmap = BitmapSource.Create(targetWidth, targetHeight, source.DpiX, source.DpiY,
            PixelFormats.Bgra32, null, output, targetStride);
        bitmap.Freeze(); return bitmap;
    }
}
