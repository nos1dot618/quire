using System.IO;
using System.Text;
using Quire.Editing;
using Quire.Export;
using Quire.Models;
using Quire.Storage;

namespace Quire.Tests;

public sealed class ExportTests : IDisposable
{
    // A valid 2x1 PNG, so image decoders accept it.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAAEUlEQVR4nGP4z8DwnwEIGGAMAEbmBvp5Q3ZZAAAAAElFTkSuQmCC");

    private readonly string _directory = Directory.CreateTempSubdirectory("quire-export-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static QuireDocument Sample()
    {
        var document = new QuireDocument { Title = "Release notes" };
        var image = document.Assets.Add(Png, "image", ".png");
        var attachment = document.Assets.Add("log line"u8.ToArray(), "attachment", ".txt");
        document.Cells.Add(new Cell
        {
            Name = "PR #519",
            Elements =
            {
                new MarkdownElement { Text = "## Summary\n\nFixes **null** images. See [docs](https://example.com) or [bad](javascript:alert(1))." },
                new TextElement { Text = "# not a heading\n4f2a9c1e *stars* 1. not a list" },
                new CodeElement { Language = "csharp", Text = "var x = \"```\";" },
                new LinkElement { Url = "https://github.com/example/quire/pull/519", Title = "PR #519" },
                new ImageElement { Asset = image },
                new DividerElement(),
                new AttachmentElement { Asset = attachment, FileName = "build log.txt" },
                new FileReferenceElement { Path = @"C:\logs\run.txt" },
            },
        });
        document.Cells.Add(new Cell { Name = "", Elements = { new TextElement { Text = "Unnamed cell" } } });
        return document;
    }

    [Fact]
    public void Markdown_WritesAssetsBesideTheFileAndLinksThemRelatively()
    {
        var path = Path.Combine(_directory, "notes.md");

        MarkdownExporter.Export(Sample(), path, new ExportOptions());

        var markdown = File.ReadAllText(path);
        Assert.Contains("## PR #519", markdown);
        Assert.Contains("## Summary", markdown);
        Assert.Contains("![image-001.png](notes_assets/image-001.png)", markdown);
        Assert.Contains("[build log.txt](notes_assets/build%20log.txt)", markdown);
        Assert.Contains("[PR #519](https://github.com/example/quire/pull/519)", markdown);
        Assert.Contains("`C:\\logs\\run.txt`", markdown);
        Assert.Contains("---", markdown);
        Assert.Equal(Png, File.ReadAllBytes(Path.Combine(_directory, "notes_assets", "image-001.png")));
        Assert.True(File.Exists(Path.Combine(_directory, "notes_assets", "build log.txt")));
        Assert.DoesNotContain("base64", markdown);
    }

    [Fact]
    public void Markdown_PlainTextDoesNotTurnIntoStructure()
    {
        var markdown = MarkdownExporter.Render(Sample(), new ExportOptions(), _ => "x.png");

        Assert.Contains("\\# not a heading", markdown);
        Assert.Contains("\\*stars\\*", markdown);
    }

    [Fact]
    public void Markdown_CodeFenceIsLongerThanBackticksInside()
    {
        var markdown = MarkdownExporter.Render(Sample(), new ExportOptions(), _ => "x.png");

        Assert.Contains("````csharp\nvar x = \"```\";\n````", markdown);
    }

    [Fact]
    public void CellNames_CanBeLeftOut()
    {
        var text = PlainTextExporter.Render(Sample(), new ExportOptions { IncludeCellNames = false });
        Assert.DoesNotContain("PR #519\n===", text);

        var withNames = PlainTextExporter.Render(Sample(), new ExportOptions());
        Assert.Contains("PR #519\n=======", withNames);
    }

    [Fact]
    public void PlainText_UsesPlaceholdersForBinaryContent()
    {
        var text = PlainTextExporter.Render(Sample(), new ExportOptions());

        Assert.Contains("[Image: image-001.png]", text);
        Assert.Contains("[Attachment: build log.txt]", text);
        Assert.Contains("[File: C:\\logs\\run.txt]", text);
        Assert.Contains("    var x = \"```\";", text);
        Assert.Contains("Fixes null images.", text);
        Assert.Contains("PR #519 <https://github.com/example/quire/pull/519>", text);
    }

    [Fact]
    public void Html_IsSelfContainedAndNeutralisesScriptLinks()
    {
        var html = HtmlExporter.Render(Sample(), new ExportOptions());

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<title>Release notes</title>", html);
        Assert.Contains("src=\"data:image/png;base64,", html);
        Assert.Contains("download=\"build log.txt\"", html);
        Assert.Contains("<a href=\"https://example.com\">docs</a>", html);
        Assert.DoesNotContain("javascript:", html);
        Assert.Contains("<span class=\"kw\">var</span>", html);
        Assert.Contains("&quot;```&quot;", html);
    }

    [Fact]
    public void Html_DoesNotPassThroughRawHtmlFromMarkdown()
    {
        var html = HtmlExporter.MarkdownHtml("<script>alert(1)</script>\n\nText <b>bold</b>");
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<b>", html);
    }

    [Fact]
    public void Pdf_ProducesAPdfWithTextAndImages()
    {
        var path = Path.Combine(_directory, "notes.pdf");
        var document = Sample();
        // A long document must flow over several pages.
        for (var i = 0; i < 40; i++)
            document.Cells.Add(new Cell { Name = $"Cell {i}", Elements = { new TextElement { Text = string.Join(' ', Enumerable.Repeat("lorem ipsum", 30)) } } });

        OnSta(() => PdfExporter.Export(document, path, new ExportOptions()));

        var bytes = File.ReadAllBytes(path);
        var raw = Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("%PDF-", raw);
        Assert.Matches(@"/Subtype\s*/Image", raw);
        Assert.True(CountPages(raw) > 1, "expected several pages");
    }

    [Fact]
    public void Pdf_HandlesMissingAndBrokenImages()
    {
        var path = Path.Combine(_directory, "broken.pdf");
        var document = new QuireDocument();
        var broken = document.Assets.Add("not an image"u8.ToArray(), "image", ".png");
        document.Cells.Add(new Cell
        {
            Elements = { new ImageElement { Asset = broken }, new ImageElement { Asset = "assets/missing.png" }, new MarkdownElement { Text = "" } },
        });

        OnSta(() => PdfExporter.Export(document, path, new ExportOptions()));

        Assert.StartsWith("%PDF-", Encoding.Latin1.GetString(File.ReadAllBytes(path)));
    }

    private static int CountPages(string raw) =>
        System.Text.RegularExpressions.Regex.Matches(raw, @"/Type\s*/Page[^s]").Count;

    private static void OnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
            throw new InvalidOperationException("Export failed on the STA thread.", error);
    }
}

public sealed class SearchAndSettingsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("quire-settings-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Find_SearchesNamesAndFields_InDocumentOrder_IgnoringCase()
    {
        var document = new QuireDocument();
        var first = new Cell { Name = "Release branch", Elements = { new TextElement { Text = "release/2.4 and RELEASE notes" } } };
        var link = new LinkElement { Url = "https://x.dev/release", Title = "Release page" };
        var second = new Cell { Name = "Other", Elements = { link, new CodeElement { Language = "release", Text = "" } } };
        document.Cells.Add(first);
        document.Cells.Add(second);

        var matches = DocumentSearch.Find(document, "release");

        Assert.Equal(
            [("Name", 0), ("Text", 0), ("Text", 16), ("Url", 14), ("Title", 0), ("Language", 0)],
            matches.Select(m => (m.Field, m.Index)));
        Assert.Same(link, matches[3].Element);
        Assert.Null(matches[0].Element);
    }

    [Fact]
    public void Find_EmptyQuery_FindsNothing() =>
        Assert.Empty(DocumentSearch.Find(new QuireDocument { Cells = { new Cell { Name = "x" } } }, ""));

    [Fact]
    public void Recent_IsNewestFirst_Deduplicated_AndCapped()
    {
        var settings = new Settings();
        for (var i = 0; i < 12; i++)
            settings.AddRecent(Path.Combine(_directory, $"doc{i}.quire"));
        settings.AddRecent(Path.Combine(_directory, "DOC5.quire"));

        Assert.Equal(Settings.MaxRecentDocuments, settings.RecentDocuments.Count);
        Assert.EndsWith("DOC5.quire", settings.RecentDocuments[0]);
        Assert.Single(settings.RecentDocuments, p => p.EndsWith("doc5.quire", StringComparison.OrdinalIgnoreCase));
        Assert.EndsWith("doc11.quire", settings.RecentDocuments[1]);
    }

    [Fact]
    public void Settings_RoundTrip_AndSurviveCorruptFiles()
    {
        var path = Path.Combine(_directory, "sub", "settings.json");
        var store = new SettingsStore(path);
        Assert.Equal(ThemeChoice.System, store.Load().Theme);

        var settings = new Settings { Theme = ThemeChoice.Dark, PreviewMode = false, ExportCellNames = false };
        settings.AddRecent(Path.Combine(_directory, "a.quire"));
        store.Save(settings);

        var loaded = store.Load();
        Assert.Equal((ThemeChoice.Dark, false, false), (loaded.Theme, loaded.PreviewMode, loaded.ExportCellNames));
        Assert.Single(loaded.RecentDocuments);

        File.WriteAllText(path, "{ not json");
        Assert.Equal(ThemeChoice.System, store.Load().Theme);
    }
}
