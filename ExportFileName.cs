using System.IO;

namespace PaperEraser;
internal static class ExportFileName
{
    internal static string FromTitle(string? title)
    {
        var value = (title ?? "").Trim();
        if (value.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) value = value[..^4];
        var invalid = Path.GetInvalidFileNameChars();
        value = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ');
        if (value.Length == 0) value = "错题练习";
        var stem = value.Split('.')[0].ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem) || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "123456789¹²³".Contains(stem[3]))) value = "_" + value;
        return value + ".pdf";
    }
}
