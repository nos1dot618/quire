using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using Quire.Editing;
using Quire.Models;
using MdBlock = Markdig.Syntax.Block;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
using PdfColor = MigraDoc.DocumentObjectModel.Color;
using PdfDocument = MigraDoc.DocumentObjectModel.Document;
using PdfParagraph = MigraDoc.DocumentObjectModel.Paragraph;

namespace Quire.Export;

/// <summary>
/// Builds a printable PDF from the document model with MigraDoc: real, selectable text, embedded
/// images scaled to the page, wrapped code blocks, and normal page flow. It never captures the screen.
/// </summary>
public static class PdfExporter
{
    private const string BodyFont = "Segoe UI";
    private const string CodeFont = "Consolas";

    private static readonly PdfColor Muted = new(0x6B, 0x6B, 0x6B);
    private static readonly PdfColor Rule = new(0xC8, 0xC8, 0xC8);
    private static readonly PdfColor CodeBackground = new(0xF3, 0xF3, 0xF3);
    private static readonly PdfColor LinkColor = new(0x0B, 0x62, 0xC4);

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseTaskLists()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .Build();

    static PdfExporter()
    {
        try
        {
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        }
        catch (InvalidOperationException)
        {
            // Already configured.
        }
    }

    public static void Export(QuireDocument quire, string path, ExportOptions options)
    {
        var tempFolder = Directory.CreateTempSubdirectory("quire-pdf-");
        try
        {
            var builder = new Builder(quire, tempFolder.FullName);
            var document = builder.Build(options);
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            renderer.PdfDocument.Info.Title = quire.Title;
            renderer.PdfDocument.Info.Creator = "Quire";
            renderer.Save(path);
        }
        finally
        {
            try
            {
                tempFolder.Delete(recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class Builder(QuireDocument quire, string tempFolder)
    {
        private readonly Dictionary<string, (string Path, int Width, int Height)?> _images = [];
        private Section _section = null!;
        private Unit _contentWidth;
        private Unit _contentHeight;

        public PdfDocument Build(ExportOptions options)
        {
            var document = new PdfDocument();
            document.Info.Title = quire.Title;
            DefineStyles(document);

            _section = document.AddSection();
            var setup = _section.PageSetup;
            // Letter in the US and a few others; A4 everywhere else.
            setup.PageFormat = RegionInfo.CurrentRegion.IsMetric ? PageFormat.A4 : PageFormat.Letter;
            setup.TopMargin = setup.BottomMargin = Unit.FromCentimeter(2);
            setup.LeftMargin = setup.RightMargin = Unit.FromCentimeter(2);
            PageSetup.GetPageSize(setup.PageFormat, out var pageWidth, out var pageHeight);
            _contentWidth = Unit.FromPoint(pageWidth.Point - setup.LeftMargin.Point - setup.RightMargin.Point);
            _contentHeight = Unit.FromPoint(pageHeight.Point - setup.TopMargin.Point - setup.BottomMargin.Point);

            var footer = _section.Footers.Primary.AddParagraph();
            footer.Format.Alignment = ParagraphAlignment.Center;
            footer.Format.Font.Size = 8;
            footer.Format.Font.Color = Muted;
            footer.AddPageField();
            footer.AddText(" / ");
            footer.AddNumPagesField();

            _section.AddParagraph(quire.Title, "Title");

            foreach (var (cell, showHeading) in Exporter.Cells(quire, options))
            {
                if (showHeading)
                    _section.AddParagraph(cell.Name.Trim(), "CellName");
                foreach (var element in cell.Elements)
                    AddElement(element);
                _section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(6);
            }
            return document;
        }

        private static void DefineStyles(PdfDocument document)
        {
            var normal = document.Styles[StyleNames.Normal]!;
            normal.Font.Name = BodyFont;
            normal.Font.Size = 10.5;
            normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(5);
            normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.Multiple;
            normal.ParagraphFormat.LineSpacing = 1.15;

            var title = document.Styles.AddStyle("Title", StyleNames.Normal);
            title.Font.Size = 20;
            title.Font.Bold = true;
            title.ParagraphFormat.SpaceAfter = Unit.FromPoint(14);

            var cellName = document.Styles.AddStyle("CellName", StyleNames.Normal);
            cellName.Font.Size = 9.5;
            cellName.Font.Bold = true;
            cellName.Font.Color = Muted;
            cellName.ParagraphFormat.SpaceBefore = Unit.FromPoint(8);
            cellName.ParagraphFormat.SpaceAfter = Unit.FromPoint(3);
            cellName.ParagraphFormat.KeepWithNext = true;

            var code = document.Styles.AddStyle("Code", StyleNames.Normal);
            code.Font.Name = CodeFont;
            code.Font.Size = 8.5;
            code.ParagraphFormat.LineSpacingRule = LineSpacingRule.Single;
            code.ParagraphFormat.Shading.Color = CodeBackground;
            code.ParagraphFormat.LeftIndent = Unit.FromPoint(6);
            code.ParagraphFormat.RightIndent = Unit.FromPoint(6);
            code.ParagraphFormat.SpaceBefore = Unit.FromPoint(2);
            code.ParagraphFormat.SpaceAfter = Unit.FromPoint(8);

            for (var level = 1; level <= 6; level++)
            {
                var heading = document.Styles.AddStyle($"MdHeading{level}", StyleNames.Normal);
                heading.Font.Bold = true;
                heading.Font.Size = level switch { 1 => 16, 2 => 14, 3 => 12.5, _ => 11 };
                heading.ParagraphFormat.SpaceBefore = Unit.FromPoint(8);
                heading.ParagraphFormat.SpaceAfter = Unit.FromPoint(4);
                heading.ParagraphFormat.KeepWithNext = true;
            }
        }

        private void AddElement(Element element)
        {
            switch (element)
            {
                case MarkdownElement markdown:
                    foreach (var block in Markdown.Parse(markdown.Text, Pipeline))
                        AddBlock(block, indent: 0);
                    break;
                case CodeElement code:
                    AddCode(code.Text, code.Language, indent: 0);
                    break;
                case TextualElement text:
                    AddPlainText(text.Text);
                    break;
                case LinkElement link:
                    AddLink(link);
                    break;
                case ImageElement or GifElement:
                    AddImage((AssetElement)element);
                    break;
                case AttachmentElement attachment:
                    var attached = _section.AddParagraph();
                    attached.AddFormattedText("Attachment: ").Color = Muted;
                    attached.AddText(Exporter.AssetLabel(attachment));
                    break;
                case FileReferenceElement reference when reference.Path.Length > 0:
                    var file = _section.AddParagraph();
                    file.AddFormattedText("File: ").Color = Muted;
                    file.AddFormattedText(reference.Path).Font.Name = CodeFont;
                    break;
                case DividerElement:
                    AddRule();
                    break;
                case UnknownElement unknown:
                    _section.AddParagraph().AddFormattedText($"[Unsupported element: {unknown.OriginalType}]").Color = Muted;
                    break;
            }
        }

        private void AddPlainText(string text)
        {
            var paragraph = _section.AddParagraph();
            var lines = text.Replace("\r\n", "\n").Trim('\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                    paragraph.AddLineBreak();
                paragraph.AddText(KeepIndentation(lines[i]));
            }
        }

        private void AddLink(LinkElement link)
        {
            var url = link.Url.Trim();
            var label = link.Title.Trim().Length > 0 ? link.Title.Trim() : url;
            var paragraph = _section.AddParagraph();
            AddHyperlink(paragraph.Elements, url, elements => elements.AddText(label));
            if (label != url && url.Length > 0)
                paragraph.AddFormattedText($"  {url}").Color = Muted;
        }

        private static void AddHyperlink(ParagraphElements target, string? url, Action<ParagraphElements> content)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto")
            {
                var hyperlink = target.AddHyperlink(uri.AbsoluteUri, HyperlinkType.Web);
                var text = hyperlink.AddFormattedText();
                text.Color = LinkColor;
                text.Underline = Underline.Single;
                content(text.Elements);
            }
            else
            {
                content(target);
            }
        }

        private void AddCode(string code, string? language, Unit indent)
        {
            var paragraph = _section.AddParagraph();
            paragraph.Style = "Code";
            paragraph.Format.LeftIndent = Unit.FromPoint(indent.Point + 6);

            var first = true;
            foreach (var line in SplitTokensByLine(SyntaxTokenizer.Tokenize(code.Replace("\r\n", "\n").TrimEnd('\n'), language)))
            {
                if (!first)
                    paragraph.AddLineBreak();
                first = false;
                var atLineStart = true;
                foreach (var token in line)
                {
                    var text = atLineStart ? KeepIndentation(token.Text) : token.Text;
                    atLineStart = false;
                    if (text.Length == 0)
                        continue;
                    var run = paragraph.AddFormattedText(text);
                    if (TokenColor(token.Kind) is { } color)
                        run.Color = color;
                }
            }
        }

        private static IEnumerable<List<Token>> SplitTokensByLine(IReadOnlyList<Token> tokens)
        {
            var line = new List<Token>();
            foreach (var token in tokens)
            {
                var parts = token.Text.Split('\n');
                for (var i = 0; i < parts.Length; i++)
                {
                    if (i > 0)
                    {
                        yield return line;
                        line = [];
                    }
                    if (parts[i].Length > 0)
                        line.Add(token with { Text = parts[i] });
                }
            }
            yield return line;
        }

        private static PdfColor? TokenColor(TokenKind kind) => kind switch
        {
            TokenKind.Keyword => new PdfColor(0x00, 0x00, 0xC8),
            TokenKind.String => new PdfColor(0xA3, 0x15, 0x15),
            TokenKind.Comment => new PdfColor(0x00, 0x80, 0x00),
            TokenKind.Number => new PdfColor(0x09, 0x86, 0x58),
            _ => null,
        };

        private void AddRule()
        {
            var rule = _section.AddParagraph();
            rule.Format.Borders.Bottom.Width = Unit.FromPoint(0.75);
            rule.Format.Borders.Bottom.Color = Rule;
            rule.Format.SpaceAfter = Unit.FromPoint(8);
            rule.Format.Font.Size = 2;
        }

        private void AddImage(AssetElement element)
        {
            if (PrepareImage(element) is not { } image)
            {
                _section.AddParagraph().AddFormattedText($"[Missing image: {element.Asset}]").Color = Muted;
                return;
            }

            // Pixels at 96 dpi, shrunk to fit the page width and most of the page height.
            var width = image.Width * 0.75;
            var height = image.Height * 0.75;
            var scale = Math.Min(1, Math.Min(_contentWidth.Point / width, _contentHeight.Point * 0.9 / height));

            var paragraph = _section.AddParagraph();
            var picture = paragraph.AddImage(image.Path);
            picture.LockAspectRatio = true;
            picture.Width = Unit.FromPoint(width * scale);
        }

        /// <summary>
        /// Writes the asset as a PNG or JPEG file the PDF library can embed. GIFs use their first
        /// frame. Returns null when the asset is missing or not a readable image.
        /// </summary>
        private (string Path, int Width, int Height)? PrepareImage(AssetElement element)
        {
            if (_images.TryGetValue(element.Asset, out var cached))
                return cached;

            (string, int, int)? result = null;
            if (quire.Assets.TryGet(element.Asset, out var data))
            {
                try
                {
                    var decoder = BitmapDecoder.Create(new MemoryStream(data, writable: false),
                        BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var frame = decoder.Frames[0];
                    var isJpeg = decoder is JpegBitmapDecoder;
                    var path = Path.Combine(tempFolder, $"{_images.Count}{(isJpeg ? ".jpg" : ".png")}");
                    if (isJpeg)
                    {
                        File.WriteAllBytes(path, data);
                    }
                    else
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(frame));
                        using var output = File.Create(path);
                        encoder.Save(output);
                    }
                    result = (path, frame.PixelWidth, frame.PixelHeight);
                }
                catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException or IOException)
                {
                    result = null;
                }
            }

            _images[element.Asset] = result;
            return result;
        }

        // --- Markdown -----------------------------------------------------------

        private void AddBlock(MdBlock block, Unit indent, string? listMarker = null, bool quoted = false)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    var title = _section.AddParagraph();
                    title.Style = $"MdHeading{Math.Clamp(heading.Level, 1, 6)}";
                    title.Format.LeftIndent = indent;
                    AddInlines(title.Elements, heading.Inline);
                    break;

                case ParagraphBlock paragraphBlock:
                    var paragraph = _section.AddParagraph();
                    paragraph.Format.LeftIndent = indent;
                    if (quoted)
                    {
                        paragraph.Format.Borders.Left.Width = Unit.FromPoint(2);
                        paragraph.Format.Borders.Left.Color = Rule;
                        paragraph.Format.Borders.DistanceFromLeft = Unit.FromPoint(6);
                        paragraph.Format.Font.Color = Muted;
                    }
                    if (listMarker is not null)
                    {
                        paragraph.Format.FirstLineIndent = Unit.FromPoint(-14);
                        paragraph.Format.LeftIndent = Unit.FromPoint(indent.Point + 14);
                        paragraph.Format.SpaceAfter = Unit.FromPoint(2);
                        paragraph.AddText(listMarker);
                        paragraph.AddTab();
                        paragraph.Format.TabStops.AddTabStop(Unit.FromPoint(indent.Point + 14));
                    }
                    AddInlines(paragraph.Elements, paragraphBlock.Inline);
                    break;

                case ListBlock list:
                    var number = list.IsOrdered && int.TryParse(list.OrderedStart, out var start) ? start : 1;
                    foreach (var item in list.OfType<ListItemBlock>())
                    {
                        var marker = list.IsOrdered ? $"{number++}." : "•";
                        if (item.Descendants<TaskList>().Any())
                            marker = "";
                        var firstChild = true;
                        foreach (var child in item)
                        {
                            AddBlock(child, Unit.FromPoint(indent.Point + (firstChild ? 0 : 14)), firstChild && child is ParagraphBlock ? marker : null);
                            firstChild = false;
                        }
                    }
                    break;

                case QuoteBlock quote:
                    foreach (var child in quote)
                        AddBlock(child, Unit.FromPoint(indent.Point + 8), quoted: true);
                    break;

                case FencedCodeBlock fenced:
                    AddCode(fenced.Lines.ToString(), fenced.Info, indent);
                    break;

                case CodeBlock code:
                    AddCode(code.Lines.ToString(), null, indent);
                    break;

                case ThematicBreakBlock:
                    AddRule();
                    break;

                case MdTable table:
                    AddTable(table, indent);
                    break;

                case ContainerBlock container:
                    foreach (var child in container)
                        AddBlock(child, indent);
                    break;

                case LeafBlock leaf:
                    var raw = _section.AddParagraph();
                    raw.Format.LeftIndent = indent;
                    raw.AddFormattedText(leaf.Lines.ToString()).Color = Muted;
                    break;
            }
        }

        private void AddTable(MdTable source, Unit indent)
        {
            var rows = source.OfType<MdTableRow>().ToList();
            var columns = rows.Select(r => r.Count).DefaultIfEmpty(0).Max();
            if (columns == 0)
                return;

            var table = _section.AddTable();
            table.Borders.Width = Unit.FromPoint(0.5);
            table.Borders.Color = Rule;
            table.Rows.LeftIndent = indent;
            var width = Unit.FromPoint((_contentWidth.Point - indent.Point) / columns);
            for (var i = 0; i < columns; i++)
                table.AddColumn(width);

            foreach (var sourceRow in rows)
            {
                var row = table.AddRow();
                row.HeadingFormat = sourceRow.IsHeader;
                row.Format.Font.Bold = sourceRow.IsHeader;
                var index = 0;
                foreach (var sourceCell in sourceRow.OfType<MdTableCell>())
                {
                    var paragraph = row.Cells[index++].AddParagraph();
                    foreach (var paragraphBlock in sourceCell.OfType<ParagraphBlock>())
                        AddInlines(paragraph.Elements, paragraphBlock.Inline);
                }
            }
            _section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(4);
        }

        private static void AddInlines(ParagraphElements target, ContainerInline? container)
        {
            if (container is null)
                return;
            foreach (var inline in container)
                AddInline(target, inline);
        }

        private static void AddInline(ParagraphElements target, Inline inline)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    target.AddText(literal.Content.ToString());
                    break;
                case CodeInline code:
                    var codeText = target.AddFormattedText(code.Content);
                    codeText.Font.Name = CodeFont;
                    codeText.Font.Size = 9;
                    break;
                case LineBreakInline lineBreak:
                    if (lineBreak.IsHard)
                        target.AddLineBreak();
                    else
                        target.AddText(" ");
                    break;
                case TaskList task:
                    // Segoe UI has no ballot box glyphs; Segoe UI Symbol does.
                    target.AddFormattedText(task.Checked ? "☑" : "☐").Font.Name = "Segoe UI Symbol";
                    target.AddText(" ");
                    break;
                case LinkInline { IsImage: true } image:
                    var alt = target.AddFormattedText();
                    alt.Color = Muted;
                    alt.AddText("[image: ");
                    AddInlines(alt.Elements, image);
                    alt.AddText("]");
                    break;
                case LinkInline link:
                    AddHyperlink(target, link.Url, elements =>
                    {
                        if (link.FirstChild is null)
                            elements.AddText(link.Url ?? "");
                        else
                            AddInlines(elements, link);
                    });
                    break;
                case AutolinkInline autolink:
                    AddHyperlink(target, autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url, elements => elements.AddText(autolink.Url));
                    break;
                case EmphasisInline emphasis:
                    var formatted = target.AddFormattedText();
                    if (emphasis.DelimiterChar != '~')
                    {
                        if (emphasis.DelimiterCount >= 2)
                            formatted.Bold = true;
                        else
                            formatted.Italic = true;
                    }
                    AddInlines(formatted.Elements, emphasis);
                    break;
                case HtmlInline html:
                    target.AddText(html.Tag);
                    break;
                case HtmlEntityInline entity:
                    target.AddText(entity.Transcoded.ToString());
                    break;
                case ContainerInline container:
                    AddInlines(target, container);
                    break;
            }
        }

        /// <summary>PDF text collapses nothing, but leading spaces can be dropped at line starts; non-breaking spaces keep them.</summary>
        private static string KeepIndentation(string line)
        {
            var count = 0;
            while (count < line.Length && (line[count] == ' ' || line[count] == '\t'))
                count++;
            if (count == 0)
                return line;
            var indent = line[..count].Replace("\t", "    ").Replace(' ', ' ');
            return indent + line[count..];
        }
    }
}
