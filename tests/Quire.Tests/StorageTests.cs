using System.IO;
using System.IO.Compression;
using System.Text;
using Quire.Models;
using Quire.Storage;

namespace Quire.Tests;

public sealed class StorageTests : IDisposable
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] GifBytes = Encoding.ASCII.GetBytes("GIF89a-not-really");

    private readonly string _directory = Directory.CreateTempSubdirectory("quire-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string PathFor(string name) => Path.Combine(_directory, name);

    private static QuireDocument CreateMixedDocument()
    {
        var document = new QuireDocument { Title = "PR 519 notes" };
        var image = document.Assets.Add(PngBytes, "image", ".png");
        var gif = document.Assets.Add(GifBytes, "animation", "gif");
        var attachment = document.Assets.Add("hello"u8.ToArray(), "attachment", ".txt");

        document.Cells.Add(new Cell
        {
            Name = "Test failure",
            Elements =
            {
                new MarkdownElement { Text = "## Heading" },
                new TextElement { Text = "Only fails on CI." },
                new ImageElement { Asset = image },
                new CodeElement { Language = "powershell", Text = "dotnet test" },
                new LinkElement { Url = "https://github.com/example/quire/pull/519", Title = "PR #519" },
                new DividerElement(),
                new GifElement { Asset = gif },
            },
        });
        document.Cells.Add(new Cell
        {
            Name = "",
            Elements =
            {
                new FileReferenceElement { Path = @"C:\temp\log.txt" },
                new AttachmentElement { Asset = attachment, FileName = "notes.txt" },
            },
        });
        return document;
    }

    [Fact]
    public void SaveThenLoad_PreservesCellsElementsAndAssets()
    {
        var original = CreateMixedDocument();
        var path = PathFor("doc.quire");

        DocumentStore.Save(original, path);
        var loaded = DocumentStore.Load(path);

        Assert.Equal("PR 519 notes", loaded.Title);
        Assert.Empty(loaded.MissingAssets);
        Assert.Equal(original.Cells.Count, loaded.Cells.Count);

        for (var c = 0; c < original.Cells.Count; c++)
        {
            var expected = original.Cells[c];
            var actual = loaded.Cells[c];
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.Elements.Select(e => (e.Id, e.TypeName)), actual.Elements.Select(e => (e.Id, e.TypeName)));
        }

        var cell = loaded.Cells[0];
        Assert.Equal("## Heading", Assert.IsType<MarkdownElement>(cell.Elements[0]).Text);
        var code = Assert.IsType<CodeElement>(cell.Elements[3]);
        Assert.Equal(("powershell", "dotnet test"), (code.Language, code.Text));
        var link = Assert.IsType<LinkElement>(cell.Elements[4]);
        Assert.Equal("PR #519", link.Title);

        var image = Assert.IsType<ImageElement>(cell.Elements[2]);
        Assert.True(loaded.Assets.TryGet(image.Asset, out var imageData));
        Assert.Equal(PngBytes, imageData);

        var attachment = Assert.IsType<AttachmentElement>(loaded.Cells[1].Elements[1]);
        Assert.Equal("notes.txt", attachment.FileName);
        Assert.True(loaded.Assets.TryGet(attachment.Asset, out var attachmentData));
        Assert.Equal("hello"u8.ToArray(), attachmentData);
    }

    [Fact]
    public void Save_StoresAssetsAsSeparateArchiveEntries()
    {
        var path = PathFor("doc.quire");
        DocumentStore.Save(CreateMixedDocument(), path);

        using var archive = ZipFile.OpenRead(path);
        var names = archive.Entries.Select(e => e.FullName).Order().ToArray();
        Assert.Equal(["assets/animation-001.gif", "assets/attachment-001.txt", "assets/image-001.png", "document.json"], names);

        using var reader = new StreamReader(archive.GetEntry("document.json")!.Open());
        var json = reader.ReadToEnd();
        Assert.Contains("\"formatVersion\": 1", json);
        Assert.DoesNotContain(Convert.ToBase64String(PngBytes), json);
    }

    [Fact]
    public void Save_DropsAssetsNoLongerReferenced()
    {
        var document = CreateMixedDocument();
        document.Cells[0].Elements.RemoveAt(2); // the image
        var path = PathFor("doc.quire");

        DocumentStore.Save(document, path);

        using var archive = ZipFile.OpenRead(path);
        Assert.Null(archive.GetEntry("assets/image-001.png"));
    }

    [Fact]
    public void Save_OverExistingFile_ReplacesItAndLeavesNoTempFiles()
    {
        var path = PathFor("doc.quire");
        DocumentStore.Save(CreateMixedDocument(), path);

        var second = new QuireDocument { Title = "Second" };
        DocumentStore.Save(second, path);

        Assert.Equal("Second", DocumentStore.Load(path).Title);
        Assert.Equal(["doc.quire"], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact]
    public void Save_WhenTargetIsLocked_LeavesExistingFileIntact()
    {
        var path = PathFor("doc.quire");
        DocumentStore.Save(CreateMixedDocument(), path);
        var before = File.ReadAllBytes(path);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => DocumentStore.Save(new QuireDocument { Title = "New" }, path));
        }

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(["doc.quire"], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact]
    public void Load_MissingAsset_IsReportedNotFatal()
    {
        var path = PathFor("missing.quire");
        WriteArchive(path, """
            {"formatVersion":1,"title":"x","cells":[{"id":"c1","name":"n","elements":[
              {"id":"e1","type":"image","asset":"assets/image-001.png"}]}]}
            """);

        var document = DocumentStore.Load(path);

        Assert.Equal(["assets/image-001.png"], document.MissingAssets);
        Assert.IsType<ImageElement>(Assert.Single(document.Cells[0].Elements));
    }

    [Fact]
    public void Load_AssetPathOutsideAssetsFolder_IsTreatedAsMissing()
    {
        var path = PathFor("escape.quire");
        WriteArchive(path, """
            {"formatVersion":1,"cells":[{"elements":[{"type":"image","asset":"../../secret.png"}]}]}
            """, ("../../secret.png", PngBytes));

        var document = DocumentStore.Load(path);

        Assert.Equal(["../../secret.png"], document.MissingAssets);
    }

    [Fact]
    public void UnknownElementType_SurvivesRoundTrip()
    {
        var path = PathFor("unknown.quire");
        WriteArchive(path, """
            {"formatVersion":1,"cells":[{"id":"c1","name":"x","elements":[
              {"id":"e1","type":"hologram","depth":3}]}]}
            """);

        var document = DocumentStore.Load(path);
        var unknown = Assert.IsType<UnknownElement>(Assert.Single(document.Cells[0].Elements));
        Assert.Equal("hologram", unknown.TypeName);

        DocumentStore.Save(document, path);
        var json = ReadManifest(path);
        Assert.Contains("\"hologram\"", json);
        Assert.Contains("\"depth\": 3", json);
    }

    [Theory]
    [InlineData("""{"formatVersion":999,"cells":[]}""", "newer version")]
    [InlineData("""{"formatVersion":1,"cells":[ """, "not valid JSON")]
    [InlineData("""{"cells":[]}""", "no formatVersion")]
    [InlineData("""{"formatVersion":1}""", "no cells")]
    [InlineData("""[1,2,3]""", "must be a JSON object")]
    [InlineData("""{"formatVersion":1,"cells":[{"elements":[{"text":"no type"}]}]}""", "no type")]
    public void Load_InvalidManifest_ThrowsFormatException(string manifest, string expectedMessage)
    {
        var path = PathFor("bad.quire");
        WriteArchive(path, manifest);

        var ex = Assert.Throws<QuireFormatException>(() => DocumentStore.Load(path));
        Assert.Contains(expectedMessage, ex.Message);
    }

    [Fact]
    public void Load_NotAZip_ThrowsFormatException()
    {
        var path = PathFor("text.quire");
        File.WriteAllText(path, "plain text");

        Assert.Throws<QuireFormatException>(() => DocumentStore.Load(path));
    }

    [Fact]
    public void Load_ZipWithoutManifest_ThrowsFormatException()
    {
        var path = PathFor("empty.quire");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            archive.CreateEntry("readme.txt");

        var ex = Assert.Throws<QuireFormatException>(() => DocumentStore.Load(path));
        Assert.Contains("document.json", ex.Message);
    }

    private static void WriteArchive(string path, string manifest, params (string Name, byte[] Data)[] entries)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("document.json").Open()))
            writer.Write(manifest);
        foreach (var (name, data) in entries)
        {
            using var stream = archive.CreateEntry(name).Open();
            stream.Write(data);
        }
    }

    private static string ReadManifest(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        using var reader = new StreamReader(archive.GetEntry("document.json")!.Open());
        return reader.ReadToEnd();
    }
}
