using System.IO;
using Quire.Editing;
using Quire.Models;
using Quire.UI;
using TextRange = System.Windows.Documents.TextRange;

namespace Quire.Tests;

public sealed class ContentRecognizerTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("quire-content-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("https://github.com/example/quire/pull/519", true)]
    [InlineData("  http://localhost:5000/api  \r\n", true)]
    [InlineData("see https://example.com", false)]
    [InlineData("https://a.com\nhttps://b.com", false)]
    [InlineData("4f2a9c1e8b7d6a5f3e2d1c0b9a8f7e6d5c4b3a21", false)]
    [InlineData("feature/cell-reorder", false)]
    [InlineData(@"C:\Users\me\file.txt", false)]
    [InlineData("file:///C:/x.txt", false)]
    [InlineData("example.com", false)]
    public void IsStandaloneUrl_OnlyAcceptsALoneWebAddress(string text, bool expected) =>
        Assert.Equal(expected, ContentRecognizer.IsStandaloneUrl(text));

    [Fact]
    public void FromText_MakesLinksOnlyForUrls()
    {
        Assert.Equal("https://x.dev/a", Assert.IsType<LinkElement>(ContentRecognizer.FromText(" https://x.dev/a ")).Url);
        Assert.Equal("main", Assert.IsType<TextElement>(ContentRecognizer.FromText("main")).Text);
    }

    [Fact]
    public void FromImage_DetectsGifsByContent()
    {
        var document = new QuireDocument();

        var gif = ContentRecognizer.FromImage(document, "GIF89a...."u8.ToArray(), ".png");
        var png = ContentRecognizer.FromImage(document, [0x89, 0x50, 0x4E, 0x47], ".png");

        Assert.IsType<GifElement>(gif);
        Assert.Equal("assets/animation-001.gif", gif.Asset);
        Assert.IsType<ImageElement>(png);
        Assert.Equal("assets/image-001.png", png.Asset);
    }

    [Fact]
    public void FromFiles_KeepsOrder_EmbedsImages_AndFollowsTheMode()
    {
        var image = Write("shot.png", [0x89, 0x50]);
        var log = Write("build.log", "log"u8.ToArray());
        var folder = Directory.CreateDirectory(Path.Combine(_directory, "folder")).FullName;
        var document = new QuireDocument();

        var referenced = ContentRecognizer.FromFiles(document, [log, image, folder], FileInsertMode.Reference);
        Assert.Collection(referenced,
            e => Assert.Equal(log, Assert.IsType<FileReferenceElement>(e).Path),
            e => Assert.IsType<ImageElement>(e),
            e => Assert.Equal(folder, Assert.IsType<FileReferenceElement>(e).Path));

        var attached = ContentRecognizer.FromFiles(document, [log], FileInsertMode.Attach);
        var attachment = Assert.IsType<AttachmentElement>(Assert.Single(attached));
        Assert.Equal("build.log", attachment.FileName);
        Assert.True(document.Assets.TryGet(attachment.Asset, out var data));
        Assert.Equal("log"u8.ToArray(), data);
    }

    [Fact]
    public void ConvertElement_KeepsIdPositionAndText()
    {
        var cell = new Cell();
        var text = new TextElement { Text = "## Notes" };
        cell.Elements.Add(new DividerElement());
        cell.Elements.Add(text);

        var markdown = DocumentEditor.ConvertElement(cell, text, ElementTypes.Markdown);

        var converted = Assert.IsType<MarkdownElement>(markdown);
        Assert.Same(converted, cell.Elements[1]);
        Assert.Equal(text.Id, converted.Id);
        Assert.Equal("## Notes", converted.Text);
        Assert.Null(DocumentEditor.ConvertElement(cell, cell.Elements[0], ElementTypes.Text));
        Assert.Null(DocumentEditor.ConvertElement(cell, converted, ElementTypes.Markdown));
    }

    [Fact]
    public void InsertElements_KeepsTheirOrderAfterTheTarget()
    {
        var cell = new Cell();
        var first = new TextElement { Text = "first" };
        var last = new TextElement { Text = "last" };
        cell.Elements.Add(first);
        cell.Elements.Add(last);

        DocumentEditor.InsertElements(cell, [new TextElement { Text = "a" }, new TextElement { Text = "b" }], after: first);

        Assert.Equal(["first", "a", "b", "last"], cell.Elements.Cast<TextElement>().Select(e => e.Text));
    }

    private string Write(string name, byte[] data)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, data);
        return path;
    }
}

public sealed class SyntaxTokenizerTests
{
    [Fact]
    public void Tokenize_CSharp_FindsKeywordsStringsCommentsAndNumbers()
    {
        var tokens = SyntaxTokenizer.Tokenize("if (x == \"a\") return 42; // done", "csharp");

        Assert.Contains(new Token("if", TokenKind.Keyword), tokens);
        Assert.Contains(new Token("return", TokenKind.Keyword), tokens);
        Assert.Contains(new Token("\"a\"", TokenKind.String), tokens);
        Assert.Contains(new Token("42", TokenKind.Number), tokens);
        Assert.Contains(new Token("// done", TokenKind.Comment), tokens);
        Assert.Equal("if (x == \"a\") return 42; // done", string.Concat(tokens.Select(t => t.Text)));
    }

    [Fact]
    public void Tokenize_Sql_KeywordsAreCaseInsensitive()
    {
        var tokens = SyntaxTokenizer.Tokenize("SELECT id FROM cells -- all", "sql");

        Assert.Contains(new Token("SELECT", TokenKind.Keyword), tokens);
        Assert.Contains(new Token("FROM", TokenKind.Keyword), tokens);
        Assert.Contains(new Token("-- all", TokenKind.Comment), tokens);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown-language")]
    [InlineData(null)]
    public void Tokenize_UnknownLanguage_IsOnePlainToken(string? language)
    {
        var tokens = SyntaxTokenizer.Tokenize("int x = 1;", language);
        Assert.Equal([new Token("int x = 1;", TokenKind.Plain)], tokens);
    }

    [Fact]
    public void Tokenize_UnterminatedStringAndComment_DoNotThrowOrLoseText()
    {
        const string code = "var s = \"never closed\n/* open comment";
        var tokens = SyntaxTokenizer.Tokenize(code, "js");
        Assert.Equal(code, string.Concat(tokens.Select(t => t.Text)));
    }
}

public sealed class MarkdownRendererTests
{
    /// <summary>WPF document objects must be created on an STA thread.</summary>
    private static T OnSta<T>(Func<T> action)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
            throw new InvalidOperationException("Rendering failed on the STA thread.", error);
        return result;
    }

    [Fact]
    public void Render_CoversCommonMarkdown()
    {
        const string markdown = """
            # Title
            Some *italic*, **bold**, ~~struck~~ and `code`, plus [a link](https://example.com) and https://auto.link

            > quoted

            1. one
               - nested
            2. two

            - [x] done
            - [ ] todo

            | A | B |
            |---|---|
            | 1 | 2 |

            ```csharp
            var x = 1;
            ```

            ---

            ![alt text](missing.png)

            <div>html block</div>
            """;

        var summary = OnSta(() =>
        {
            var document = MarkdownRenderer.Render(markdown);
            var text = new TextRange(document.ContentStart, document.ContentEnd).Text;
            return (Blocks: document.Blocks.Count, Text: text);
        });

        Assert.True(summary.Blocks >= 8);
        Assert.Contains("Title", summary.Text);
        Assert.Contains("a link", summary.Text);
        Assert.Contains("var x = 1;", summary.Text);
        Assert.Contains("[image: alt text]", summary.Text);
        Assert.Contains("\u2611", summary.Text);
    }

    [Fact]
    public void Render_EmptyAndMalformedInput_DoesNotThrow()
    {
        OnSta(() => MarkdownRenderer.Render(""));
        OnSta(() => MarkdownRenderer.Render("**unclosed [link( `tick\n|a|\n|-"));
    }
}
