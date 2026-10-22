using System.Text;
using System.Text.RegularExpressions;
using Quire.Models;

namespace Quire.Export;

/// <summary>
/// Writes a .md file. Images and attachments are copied into a "&lt;name&gt;_assets" folder beside it
/// and linked with relative paths; the Markdown file itself contains no binary data.
/// </summary>
public static partial class MarkdownExporter
{
    public static void Export(QuireDocument document, string path, ExportOptions options)
    {
        var assets = new AssetFolder(document, path);
        Exporter.WriteText(path, Render(document, options, assets.RelativePath));
    }

    /// <param name="assetPath">Returns the relative path an asset was written to, or null if it is missing.</param>
    public static string Render(QuireDocument document, ExportOptions options, Func<AssetElement, string?> assetPath)
    {
        var blocks = new List<string>();
        foreach (var (cell, showHeading) in Exporter.Cells(document, options))
        {
            if (showHeading)
                blocks.Add("## " + EscapeInline(cell.Name.Trim()));

            foreach (var element in cell.Elements)
            {
                if (RenderElement(element, assetPath) is { Length: > 0 } block)
                    blocks.Add(block);
            }
        }
        return string.Join("\n\n", blocks) + "\n";
    }

    private static string? RenderElement(Element element, Func<AssetElement, string?> assetPath) => element switch
    {
        MarkdownElement markdown => Normalize(markdown.Text).Trim('\n'),
        CodeElement code => Fence(code.Text, code.Language),
        TextualElement text => PlainText(text.Text),
        LinkElement link => Link(link),
        ImageElement or GifElement => assetPath((AssetElement)element) is { } image
            ? $"![{EscapeInline(Exporter.AssetLabel((AssetElement)element))}]({EscapeUrl(image)})"
            : $"*[Missing image: {EscapeInline(((AssetElement)element).Asset)}]*",
        AttachmentElement attachment => assetPath(attachment) is { } file
            ? $"Attachment: [{EscapeInline(Exporter.AssetLabel(attachment))}]({EscapeUrl(file)})"
            : $"*[Missing attachment: {EscapeInline(Exporter.AssetLabel(attachment))}]*",
        FileReferenceElement reference => reference.Path.Length == 0 ? null : InlineCode(reference.Path),
        DividerElement => "---",
        UnknownElement unknown => $"<!-- Unsupported element: {unknown.OriginalType.Replace("--", "- -")} -->",
        _ => null,
    };

    private static string Link(LinkElement link)
    {
        var url = link.Url.Trim();
        if (url.Length == 0)
            return EscapeInline(link.Title);
        return link.Title.Trim().Length == 0 ? $"<{url}>" : $"[{EscapeInline(link.Title.Trim())}]({EscapeUrl(url)})";
    }

    /// <summary>
    /// Plain text stays readable but must not turn into Markdown structure: line-start markers and
    /// inline emphasis characters are escaped, and line breaks are kept as hard breaks.
    /// </summary>
    private static string PlainText(string text)
    {
        var lines = Normalize(text).Trim('\n').Split('\n');
        var output = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = EscapeLineStart(EscapeInline(lines[i]));
            output.Append(line);
            if (i < lines.Length - 1)
                output.Append(line.Length == 0 ? "\n" : "\\\n");
        }
        return output.ToString();
    }

    /// <summary>A fenced code block whose fence is longer than any backtick run in the code.</summary>
    private static string Fence(string code, string language)
    {
        var longest = BacktickRuns().Matches(code).Select(m => m.Length).DefaultIfEmpty(0).Max();
        var fence = new string('`', Math.Max(3, longest + 1));
        var info = language.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return $"{fence}{info}\n{Normalize(code).TrimEnd('\n')}\n{fence}";
    }

    private static string InlineCode(string text)
    {
        var longest = BacktickRuns().Matches(text).Select(m => m.Length).DefaultIfEmpty(0).Max();
        var ticks = new string('`', longest + 1);
        var pad = text.StartsWith('`') || text.EndsWith('`') ? " " : "";
        return $"{ticks}{pad}{text}{pad}{ticks}";
    }

    private static string EscapeInline(string text) => InlineSpecials().Replace(text, @"\$0");

    private static string EscapeLineStart(string line)
    {
        var match = LineStartMarker().Match(line);
        if (!match.Success)
            return line;
        var marker = match.Groups["marker"];
        // "1." becomes "1\." (escaping the digit would not stop the list); other markers get a leading backslash.
        var at = char.IsDigit(marker.Value[0]) ? marker.Index + marker.Length - 1 : marker.Index;
        return line.Insert(at, "\\");
    }

    private static string EscapeUrl(string url) => url.Replace(" ", "%20").Replace("(", "%28").Replace(")", "%29");

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    [GeneratedRegex("`+")]
    private static partial Regex BacktickRuns();

    [GeneratedRegex(@"[\\`*_\[\]<>|]")]
    private static partial Regex InlineSpecials();

    // Headings, quotes, list bullets, ordered list numbers, and thematic breaks.
    [GeneratedRegex(@"^\s{0,3}(?<marker>#|>|[-+=]|\d+[.)](?=\s|$))")]
    private static partial Regex LineStartMarker();
}
