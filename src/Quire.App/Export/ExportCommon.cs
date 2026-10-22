using System.IO;
using Quire.Models;

namespace Quire.Export;

public sealed record ExportOptions
{
    /// <summary>Write each named cell's name as a heading.</summary>
    public bool IncludeCellNames { get; init; } = true;
}

public enum ExportFormat
{
    Markdown,
    Html,
    Pdf,
    PlainText,
}

public static class Exporter
{
    public static string Extension(ExportFormat format) => format switch
    {
        ExportFormat.Markdown => ".md",
        ExportFormat.Html => ".html",
        ExportFormat.Pdf => ".pdf",
        _ => ".txt",
    };

    public static string Filter(ExportFormat format) => format switch
    {
        ExportFormat.Markdown => "Markdown (*.md)|*.md",
        ExportFormat.Html => "Web page (*.html)|*.html",
        ExportFormat.Pdf => "PDF (*.pdf)|*.pdf",
        _ => "Plain text (*.txt)|*.txt",
    };

    /// <summary>Exports a document. Works only from the document model, never from the UI.</summary>
    public static void Export(QuireDocument document, string path, ExportFormat format, ExportOptions options)
    {
        switch (format)
        {
            case ExportFormat.Markdown:
                MarkdownExporter.Export(document, path, options);
                break;
            case ExportFormat.Html:
                WriteText(path, HtmlExporter.Render(document, options));
                break;
            case ExportFormat.Pdf:
                PdfExporter.Export(document, path, options);
                break;
            default:
                WriteText(path, PlainTextExporter.Render(document, options));
                break;
        }
    }

    internal static void WriteText(string path, string contents) =>
        File.WriteAllText(path, contents, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    internal static IEnumerable<(Cell Cell, bool ShowHeading)> Cells(QuireDocument document, ExportOptions options) =>
        document.Cells.Select(cell => (cell, options.IncludeCellNames && !string.IsNullOrWhiteSpace(cell.Name)));

    /// <summary>A short label for an asset-backed element, used where binary content cannot be shown.</summary>
    internal static string AssetLabel(AssetElement element) => element switch
    {
        AttachmentElement { FileName.Length: > 0 } attachment => attachment.FileName,
        _ => Path.GetFileName(element.Asset),
    };
}

/// <summary>
/// Copies assets next to an exported file, into a folder named after it, and hands back relative
/// paths for links. Each asset is written once even when several elements use it.
/// </summary>
internal sealed class AssetFolder(QuireDocument document, string exportPath)
{
    private readonly Dictionary<string, string> _written = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    public string FolderName { get; } = Path.GetFileNameWithoutExtension(exportPath) + "_assets";

    private string FolderPath => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exportPath))!, FolderName);

    /// <summary>Writes the asset if needed and returns its relative path with forward slashes, or null if it is missing.</summary>
    public string? RelativePath(AssetElement element)
    {
        if (_written.TryGetValue(element.Asset, out var existing))
            return existing;
        if (!document.Assets.TryGet(element.Asset, out var data))
            return null;

        Directory.CreateDirectory(FolderPath);
        var name = UniqueName(SafeFileName(Exporter.AssetLabel(element)));
        File.WriteAllBytes(Path.Combine(FolderPath, name), data);
        var relative = $"{FolderName}/{name}";
        _written[element.Asset] = relative;
        return relative;
    }

    private string UniqueName(string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        var candidate = name;
        for (var n = 2; !_names.Add(candidate); n++)
            candidate = $"{stem}-{n}{extension}";
        return candidate;
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        return cleaned.Length == 0 ? "file" : cleaned;
    }
}
