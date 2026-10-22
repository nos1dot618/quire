using System.Text;
using Markdig;
using Quire.Models;

namespace Quire.Export;

/// <summary>Flattens a document into readable text. Binary content becomes a bracketed placeholder.</summary>
public static class PlainTextExporter
{
    public static string Render(QuireDocument document, ExportOptions options)
    {
        var blocks = new List<string>();
        foreach (var (cell, showHeading) in Exporter.Cells(document, options))
        {
            if (showHeading)
            {
                var name = cell.Name.Trim();
                blocks.Add(name + "\n" + new string('=', Math.Min(name.Length, 80)));
            }

            foreach (var element in cell.Elements)
            {
                if (RenderElement(element) is { Length: > 0 } block)
                    blocks.Add(block);
            }
        }
        return string.Join("\n\n", blocks) + "\n";
    }

    private static string? RenderElement(Element element) => element switch
    {
        MarkdownElement markdown => Markdown.ToPlainText(markdown.Text).Replace("\r\n", "\n").Trim('\n'),
        CodeElement code => Indent(code.Text),
        TextualElement text => text.Text.Replace("\r\n", "\n").Trim('\n'),
        LinkElement link => link.Title.Trim().Length == 0 ? link.Url : $"{link.Title.Trim()} <{link.Url}>",
        ImageElement image => $"[Image: {Exporter.AssetLabel(image)}]",
        GifElement gif => $"[Animated image: {Exporter.AssetLabel(gif)}]",
        AttachmentElement attachment => $"[Attachment: {Exporter.AssetLabel(attachment)}]",
        FileReferenceElement reference => reference.Path.Length == 0 ? null : $"[File: {reference.Path}]",
        DividerElement => new string('-', 40),
        UnknownElement unknown => $"[Unsupported element: {unknown.OriginalType}]",
        _ => null,
    };

    /// <summary>Code is indented four spaces so it stands apart from prose.</summary>
    private static string Indent(string code)
    {
        var builder = new StringBuilder();
        foreach (var line in code.Replace("\r\n", "\n").TrimEnd('\n').Split('\n'))
            builder.Append(line.Length == 0 ? "" : "    ").Append(line).Append('\n');
        return builder.ToString().TrimEnd('\n');
    }
}
