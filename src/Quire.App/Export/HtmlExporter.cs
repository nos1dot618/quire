using System.IO;
using System.Net;
using System.Text;
using Markdig;
using Markdig.Syntax;
using Quire.Editing;
using Quire.Models;

namespace Quire.Export;

/// <summary>
/// Writes one self-contained web page: images and attachments are embedded as data URIs, so the
/// page can be mailed or moved on its own. Raw HTML inside Markdown is not passed through.
/// </summary>
public static class HtmlExporter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseTaskLists()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .DisableHtml()
        .Build();

    private const string Style = """
        :root { color-scheme: light dark; --fg: #1b1b1b; --bg: #ffffff; --muted: #6b6b6b; --rule: #d0d0d0; --code: #f3f3f3;
                --kw: #0000c8; --str: #a31515; --com: #008000; --num: #098658; --link: #0b62c4; }
        @media (prefers-color-scheme: dark) {
          :root { --fg: #e6e6e6; --bg: #1e1e1e; --muted: #9a9a9a; --rule: #3c3c3c; --code: #2a2a2a;
                  --kw: #569cd6; --str: #ce9178; --com: #6a9955; --num: #b5cea8; --link: #4ea1f3; } }
        body { margin: 0; background: var(--bg); color: var(--fg); font: 15px/1.5 "Segoe UI", system-ui, sans-serif; }
        main { max-width: 820px; margin: 0 auto; padding: 32px 16px 64px; }
        h1.title { font-size: 26px; font-weight: 600; margin: 0 0 24px; }
        section.cell { margin: 0 0 28px; }
        h2.cell-name { font-size: 13px; font-weight: 600; color: var(--muted); margin: 0 0 6px; text-transform: none; }
        .element { margin: 0 0 10px; }
        .text { white-space: pre-wrap; overflow-wrap: anywhere; }
        pre { background: var(--code); padding: 8px 10px; overflow-x: auto; margin: 0; border-radius: 0; }
        code { font-family: "Cascadia Mono", Consolas, monospace; font-size: 13px; }
        :not(pre) > code { background: var(--code); padding: 0 3px; }
        img { max-width: 100%; height: auto; display: block; }
        a { color: var(--link); }
        hr { border: 0; border-top: 1px solid var(--rule); margin: 14px 0; }
        table { border-collapse: collapse; } th, td { border: 1px solid var(--rule); padding: 3px 8px; text-align: left; }
        blockquote { margin: 0; padding-left: 12px; border-left: 3px solid var(--rule); color: var(--muted); }
        .muted { color: var(--muted); }
        .kw { color: var(--kw); } .str { color: var(--str); } .com { color: var(--com); } .num { color: var(--num); }
        """;

    public static string Render(QuireDocument document, ExportOptions options)
    {
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
            .Append("<meta name=\"generator\" content=\"Quire\">\n")
            .Append("<title>").Append(Encode(document.Title)).Append("</title>\n")
            .Append("<style>\n").Append(Style).Append("\n</style>\n</head>\n<body>\n<main>\n")
            .Append("<h1 class=\"title\">").Append(Encode(document.Title)).Append("</h1>\n");

        foreach (var (cell, showHeading) in Exporter.Cells(document, options))
        {
            html.Append("<section class=\"cell\">\n");
            if (showHeading)
                html.Append("<h2 class=\"cell-name\">").Append(Encode(cell.Name.Trim())).Append("</h2>\n");
            foreach (var element in cell.Elements)
                html.Append(RenderElement(element, document));
            html.Append("</section>\n");
        }

        html.Append("</main>\n</body>\n</html>\n");
        return html.ToString();
    }

    private static string RenderElement(Element element, QuireDocument document) => element switch
    {
        MarkdownElement markdown => Wrap("markdown", MarkdownHtml(markdown.Text)),
        CodeElement code => Wrap("code", $"<pre><code>{Highlight(code.Text, code.Language)}</code></pre>"),
        TextualElement text => Wrap("text", Encode(text.Text)),
        LinkElement link => Wrap("link", LinkHtml(link)),
        ImageElement or GifElement => Wrap("image", ImageHtml((AssetElement)element, document)),
        AttachmentElement attachment => Wrap("attachment", AttachmentHtml(attachment, document)),
        FileReferenceElement reference => reference.Path.Length == 0 ? "" : Wrap("file", $"<code>{Encode(reference.Path)}</code>"),
        DividerElement => "<hr>\n",
        UnknownElement unknown => $"<!-- Unsupported element: {Encode(unknown.OriginalType).Replace("--", "- -")} -->\n",
        _ => "",
    };

    /// <summary>Renders Markdown, neutralising link and image URLs with unsafe schemes such as javascript:.</summary>
    internal static string MarkdownHtml(string markdown)
    {
        var parsed = Markdig.Markdown.Parse(markdown, Pipeline);
        foreach (var link in parsed.Descendants<Markdig.Syntax.Inlines.LinkInline>())
        {
            if (!IsSafeUrl(link.Url))
                link.Url = "#";
        }

        using var writer = new StringWriter();
        var renderer = new Markdig.Renderers.HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(parsed);
        writer.Flush();
        return writer.ToString();
    }

    private static bool IsSafeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return true;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return !url.Contains(':'); // relative paths and fragments are fine
        return uri.Scheme is "http" or "https" or "mailto";
    }

    private static string Wrap(string kind, string inner) => $"<div class=\"element {kind}\">{inner}</div>\n";

    private static string LinkHtml(LinkElement link)
    {
        var url = link.Url.Trim();
        var label = Encode(link.Title.Trim().Length > 0 ? link.Title.Trim() : url);
        // Only web and mail links become clickable; other schemes could run code in the reader's browser.
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto"
            ? $"<a href=\"{Encode(uri.AbsoluteUri)}\">{label}</a>"
            : label;
    }

    private static string ImageHtml(AssetElement image, QuireDocument document) =>
        document.Assets.TryGet(image.Asset, out var data)
            ? $"<img src=\"data:{MimeType(image.Asset)};base64,{Convert.ToBase64String(data)}\" alt=\"{Encode(Exporter.AssetLabel(image))}\">"
            : $"<span class=\"muted\">[Missing image: {Encode(image.Asset)}]</span>";

    private static string AttachmentHtml(AttachmentElement attachment, QuireDocument document)
    {
        var name = Encode(Exporter.AssetLabel(attachment));
        if (!document.Assets.TryGet(attachment.Asset, out var data))
            return $"<span class=\"muted\">[Missing attachment: {name}]</span>";
        return $"<span class=\"muted\">Attachment:</span> <a download=\"{name}\" href=\"data:application/octet-stream;base64,{Convert.ToBase64String(data)}\">{name}</a>";
    }

    private static string Highlight(string code, string language)
    {
        var html = new StringBuilder();
        foreach (var token in SyntaxTokenizer.Tokenize(code.Replace("\r\n", "\n").TrimEnd('\n'), language))
        {
            var cls = token.Kind switch
            {
                TokenKind.Keyword => "kw",
                TokenKind.String => "str",
                TokenKind.Comment => "com",
                TokenKind.Number => "num",
                _ => null,
            };
            if (cls is null)
                html.Append(Encode(token.Text));
            else
                html.Append("<span class=\"").Append(cls).Append("\">").Append(Encode(token.Text)).Append("</span>");
        }
        return html.ToString();
    }

    internal static string MimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".webp" => "image/webp",
        ".tif" or ".tiff" => "image/tiff",
        ".ico" => "image/x-icon",
        _ => "application/octet-stream",
    };

    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
