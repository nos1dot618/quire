using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Quire.Editing;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using WpfBlock = System.Windows.Documents.Block;
using WpfInline = System.Windows.Documents.Inline;
using WpfTable = System.Windows.Documents.Table;
using WpfTableCell = System.Windows.Documents.TableCell;
using WpfTableRow = System.Windows.Documents.TableRow;

namespace Quire.UI;

/// <summary>Renders Markdown to a WPF <see cref="FlowDocument"/> for inline preview. The Markdown source is never modified.</summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseTaskLists()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .Build();

    private static readonly double[] HeadingSizes = [22, 19, 16.5, 15, 14, 14];

    public static FlowDocument Render(string markdown)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = Resource<FontFamily>("DocumentFont") ?? new FontFamily("Segoe UI"),
            FontSize = 14,
            TextAlignment = TextAlignment.Left,
        };
        document.SetResourceReference(FlowDocument.ForegroundProperty, "Quire.Text");

        foreach (var block in Markdown.Parse(markdown, Pipeline))
        {
            if (RenderBlock(block) is { } rendered)
                document.Blocks.Add(rendered);
        }

        // Trim the outer margins so the preview lines up with the source editor.
        if (document.Blocks.FirstBlock is { } first)
            first.Margin = first.Margin with { Top = 0 };
        if (document.Blocks.LastBlock is { } last)
            last.Margin = last.Margin with { Bottom = 0 };
        return document;
    }

    private static WpfBlock? RenderBlock(MdBlock block) => block switch
    {
        HeadingBlock heading => Heading(heading),
        ParagraphBlock paragraph => Paragraph(paragraph.Inline),
        QuoteBlock quote => Quote(quote),
        ListBlock list => List(list),
        FencedCodeBlock fenced => CodeBlock(fenced, fenced.Info),
        CodeBlock code => CodeBlock(code, null),
        ThematicBreakBlock => Rule(),
        Markdig.Extensions.Tables.Table table => Table(table),
        ContainerBlock container => Section(container),
        LeafBlock leaf => PlainLines(leaf),
        _ => null,
    };

    private static Paragraph Heading(HeadingBlock heading)
    {
        var paragraph = Paragraph(heading.Inline);
        paragraph.FontSize = HeadingSizes[Math.Clamp(heading.Level, 1, 6) - 1];
        paragraph.FontWeight = heading.Level <= 2 ? FontWeights.SemiBold : FontWeights.Bold;
        paragraph.Margin = new Thickness(0, 10, 0, 4);
        return paragraph;
    }

    private static Paragraph Paragraph(ContainerInline? inlines)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
        if (inlines is not null)
            AddInlines(paragraph.Inlines, inlines);
        return paragraph;
    }

    private static Section Quote(QuoteBlock quote)
    {
        var section = Section(quote);
        section.BorderThickness = new Thickness(3, 0, 0, 0);
        section.SetResourceReference(WpfBlock.BorderBrushProperty, "Quire.Rule");
        section.SetResourceReference(TextElement.ForegroundProperty, "Quire.Muted");
        section.Padding = new Thickness(10, 0, 0, 0);
        section.Margin = new Thickness(0, 0, 0, 8);
        return section;
    }

    private static Section Section(ContainerBlock container)
    {
        var section = new Section();
        foreach (var child in container)
        {
            if (RenderBlock(child) is { } rendered)
                section.Blocks.Add(rendered);
        }
        return section;
    }

    private static List List(ListBlock list)
    {
        var result = new List
        {
            MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(22, 0, 0, 0),
        };
        if (list.IsOrdered && int.TryParse(list.OrderedStart, out var start) && start > 0)
            result.StartIndex = start;

        foreach (var item in list.OfType<ListItemBlock>())
        {
            var listItem = new ListItem();
            foreach (var child in item)
            {
                if (RenderBlock(child) is not { } rendered)
                    continue;
                // Tight lists: no gap between items.
                if (rendered is Paragraph paragraph && list.IsLoose == false)
                    paragraph.Margin = new Thickness(0);
                listItem.Blocks.Add(rendered);
            }
            result.ListItems.Add(listItem);
        }

        // A task list reads better without bullets next to its check boxes.
        if (list.Descendants<TaskList>().Any())
            result.MarkerStyle = TextMarkerStyle.None;
        return result;
    }

    private static Paragraph CodeBlock(LeafBlock code, string? language)
    {
        var paragraph = new Paragraph
        {
            FontFamily = Resource<FontFamily>("CodeFont") ?? new FontFamily("Consolas"),
            FontSize = 13,
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 0, 8),
        };
        paragraph.SetResourceReference(WpfBlock.BackgroundProperty, "Quire.CodeBackground");
        AddHighlighted(paragraph.Inlines, code.Lines.ToString(), language);
        return paragraph;
    }

    private static Paragraph Rule()
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 4, 0, 12), BorderThickness = new Thickness(0, 0, 0, 1), FontSize = 1 };
        paragraph.SetResourceReference(WpfBlock.BorderBrushProperty, "Quire.Rule");
        return paragraph;
    }

    private static WpfTable Table(Markdig.Extensions.Tables.Table table)
    {
        var result = new WpfTable { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 8), BorderThickness = new Thickness(1, 1, 0, 0) };
        result.SetResourceReference(WpfBlock.BorderBrushProperty, "Quire.Rule");

        var columns = table.OfType<Markdig.Extensions.Tables.TableRow>().Select(r => r.Count).DefaultIfEmpty(0).Max();
        for (var i = 0; i < columns; i++)
            result.Columns.Add(new TableColumn());

        var group = new TableRowGroup();
        foreach (var row in table.OfType<Markdig.Extensions.Tables.TableRow>())
        {
            var wpfRow = new WpfTableRow();
            if (row.IsHeader)
                wpfRow.FontWeight = FontWeights.SemiBold;

            foreach (var cell in row.OfType<Markdig.Extensions.Tables.TableCell>())
            {
                var wpfCell = new WpfTableCell { Padding = new Thickness(6, 2, 6, 2), BorderThickness = new Thickness(0, 0, 1, 1) };
                wpfCell.SetResourceReference(WpfTableCell.BorderBrushProperty, "Quire.Rule");
                foreach (var child in cell)
                {
                    if (RenderBlock(child) is { } rendered)
                    {
                        rendered.Margin = new Thickness(0);
                        wpfCell.Blocks.Add(rendered);
                    }
                }
                wpfRow.Cells.Add(wpfCell);
            }
            group.Rows.Add(wpfRow);
        }
        result.RowGroups.Add(group);
        return result;
    }

    /// <summary>HTML blocks and anything unrecognised: shown as their source text.</summary>
    private static Paragraph PlainLines(LeafBlock leaf)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
        paragraph.SetResourceReference(TextElement.ForegroundProperty, "Quire.Muted");
        paragraph.Inlines.Add(new Run(leaf.Lines.ToString()));
        return paragraph;
    }

    private static void AddInlines(InlineCollection target, ContainerInline container)
    {
        foreach (var inline in container)
        {
            if (RenderInline(inline) is { } rendered)
                target.Add(rendered);
        }
    }

    private static WpfInline? RenderInline(MdInline inline)
    {
        switch (inline)
        {
            case LiteralInline literal:
                return new Run(literal.Content.ToString());

            case CodeInline code:
                var codeRun = new Run(code.Content)
                {
                    FontFamily = Resource<FontFamily>("CodeFont") ?? new FontFamily("Consolas"),
                };
                codeRun.SetResourceReference(TextElement.BackgroundProperty, "Quire.CodeBackground");
                return codeRun;

            case LineBreakInline lineBreak:
                return lineBreak.IsHard ? new LineBreak() : new Run(" ");

            case TaskList task:
                return new Run(task.Checked ? "☑ " : "☐ ");

            case LinkInline { IsImage: true } image:
                // Markdown images point at files outside the document; show their alt text instead of fetching them.
                var alt = new Span();
                alt.SetResourceReference(TextElement.ForegroundProperty, "Quire.Muted");
                alt.Inlines.Add(new Run("[image: "));
                AddInlines(alt.Inlines, image);
                alt.Inlines.Add(new Run("]"));
                return alt;

            case LinkInline link:
                return Hyperlink(link.Url, link.FirstChild is null ? new Run(link.Url) : Children(link));

            case AutolinkInline autolink:
                return Hyperlink(autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url, new Run(autolink.Url));

            case EmphasisInline emphasis:
                var span = Children(emphasis);
                switch (emphasis.DelimiterChar, emphasis.DelimiterCount)
                {
                    case ('~', _):
                        span.TextDecorations = TextDecorations.Strikethrough;
                        break;
                    case (_, >= 2):
                        span.FontWeight = FontWeights.Bold;
                        break;
                    default:
                        span.FontStyle = FontStyles.Italic;
                        break;
                }
                return span;

            case HtmlInline html:
                return new Run(html.Tag);

            case HtmlEntityInline entity:
                return new Run(entity.Transcoded.ToString());

            case ContainerInline container:
                return Children(container);

            default:
                return null;
        }
    }

    private static Span Children(ContainerInline container)
    {
        var span = new Span();
        AddInlines(span.Inlines, container);
        return span;
    }

    private static WpfInline Hyperlink(string? url, WpfInline content)
    {
        var link = new Hyperlink(content);
        link.SetResourceReference(TextElement.ForegroundProperty, "Quire.Link");
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            link.NavigateUri = uri;
            link.ToolTip = uri.ToString();
            link.RequestNavigate += (_, e) => LinkLauncher.Open(e.Uri.ToString());
        }
        return link;
    }

    /// <summary>Adds syntax-highlighted runs for code.</summary>
    public static void AddHighlighted(InlineCollection target, string code, string? language)
    {
        foreach (var token in SyntaxTokenizer.Tokenize(code.TrimEnd('\r', '\n'), language))
        {
            var run = new Run(token.Text);
            var key = token.Kind switch
            {
                TokenKind.Keyword => "SyntaxKeywordBrush",
                TokenKind.String => "SyntaxStringBrush",
                TokenKind.Comment => "SyntaxCommentBrush",
                TokenKind.Number => "SyntaxNumberBrush",
                _ => null,
            };
            if (key is not null)
                run.SetResourceReference(TextElement.ForegroundProperty, key);
            target.Add(run);
        }
    }

    private static T? Resource<T>(string key) where T : class =>
        Application.Current?.TryFindResource(key) as T;
}
