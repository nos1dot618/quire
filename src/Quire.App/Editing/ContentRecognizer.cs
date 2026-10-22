using System.IO;
using Quire.Models;

namespace Quire.Editing;

/// <summary>How a dropped or pasted non-image file is added to a document.</summary>
public enum FileInsertMode
{
    /// <summary>Store the path only. The file stays where it is.</summary>
    Reference,

    /// <summary>Copy the file into the document so it travels with it.</summary>
    Attach,
}

/// <summary>
/// Turns pasted or dropped content into elements. Deliberately conservative: only images,
/// files, and text that is nothing but a single URL get special treatment. Everything else
/// (SHAs, branch names, paths typed as text) stays ordinary editable text.
/// </summary>
public static class ContentRecognizer
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".ico",
    };

    public static bool IsImageFile(string path) => ImageExtensions.Contains(Path.GetExtension(path));

    public static bool IsGif(string path) => Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the whole text is one http(s) URL, with no spaces or extra lines.</summary>
    public static bool IsStandaloneUrl(string text)
    {
        text = text.Trim();
        return text.Length > 0
            && !text.Any(char.IsWhiteSpace)
            && Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && uri.Host.Length > 0;
    }

    /// <summary>Creates an element for pasted or dropped text: a link for a lone URL, plain text otherwise.</summary>
    public static Element FromText(string text) =>
        IsStandaloneUrl(text) ? new LinkElement { Url = text.Trim() } : new TextElement { Text = text };

    /// <summary>Stores image bytes as an asset and returns the matching image or GIF element.</summary>
    public static AssetElement FromImage(QuireDocument document, byte[] data, string extension)
    {
        var isGif = extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) || LooksLikeGif(data);
        if (isGif)
            return new GifElement { Asset = document.Assets.Add(data, "animation", ".gif") };
        return new ImageElement { Asset = document.Assets.Add(data, "image", extension) };
    }

    /// <summary>
    /// Creates elements for files in the order given. Images are always embedded so they display;
    /// other files follow <paramref name="mode"/>. Directories always become references.
    /// </summary>
    public static IReadOnlyList<Element> FromFiles(QuireDocument document, IEnumerable<string> paths, FileInsertMode mode)
    {
        var elements = new List<Element>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                elements.Add(new FileReferenceElement { Path = path });
            }
            else if (IsImageFile(path))
            {
                elements.Add(FromImage(document, File.ReadAllBytes(path), Path.GetExtension(path)));
            }
            else if (mode == FileInsertMode.Attach)
            {
                var asset = document.Assets.Add(File.ReadAllBytes(path), "attachment", Path.GetExtension(path));
                elements.Add(new AttachmentElement { Asset = asset, FileName = Path.GetFileName(path) });
            }
            else
            {
                elements.Add(new FileReferenceElement { Path = path });
            }
        }
        return elements;
    }

    private static bool LooksLikeGif(byte[] data) =>
        data.Length >= 6 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F' && data[3] == '8';
}
