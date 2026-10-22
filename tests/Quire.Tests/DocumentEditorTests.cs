using Quire.Editing;
using Quire.Models;

namespace Quire.Tests;

public sealed class DocumentEditorTests
{
    private static (QuireDocument Document, Cell A, Cell B, Cell C) ThreeCells()
    {
        var document = new QuireDocument();
        var a = new Cell { Name = "A" };
        var b = new Cell { Name = "B" };
        var c = new Cell { Name = "C" };
        document.Cells.Add(a);
        document.Cells.Add(b);
        document.Cells.Add(c);
        return (document, a, b, c);
    }

    private static string Names(QuireDocument document) => string.Concat(document.Cells.Select(c => c.Name));

    [Fact]
    public void InsertCell_GoesAfterTheGivenCell_OrAtTheEnd()
    {
        var (document, a, _, _) = ThreeCells();

        DocumentEditor.InsertCell(document, a, "X");
        DocumentEditor.InsertCell(document, after: null, "Y");

        Assert.Equal("AXBCY", Names(document));
    }

    [Fact]
    public void MoveUpAndDown_ReorderCells_AndStopAtTheEdges()
    {
        var (document, a, _, c) = ThreeCells();

        DocumentEditor.MoveUp(document, a);
        DocumentEditor.MoveDown(document, c);
        Assert.Equal("ABC", Names(document));

        DocumentEditor.MoveDown(document, a);
        DocumentEditor.MoveUp(document, c);
        Assert.Equal("BCA", Names(document));
    }

    [Fact]
    public void DuplicateCell_CopiesElementsWithNewIds_AndSharesAssets()
    {
        var (document, a, _, _) = ThreeCells();
        a.Elements.Add(new TextElement { Text = "sha" });
        a.Elements.Add(new ImageElement { Asset = "assets/image-001.png" });

        var copy = DocumentEditor.DuplicateCell(document, a);

        Assert.Equal("AABC", Names(document));
        Assert.NotEqual(a.Id, copy.Id);
        Assert.Equal("sha", Assert.IsType<TextElement>(copy.Elements[0]).Text);
        Assert.NotEqual(a.Elements[0].Id, copy.Elements[0].Id);
        Assert.Equal("assets/image-001.png", Assert.IsType<ImageElement>(copy.Elements[1]).Asset);
    }

    [Fact]
    public void DeleteCell_ReturnsTheNextCellToSelect()
    {
        var (document, a, b, c) = ThreeCells();

        Assert.Same(c, DocumentEditor.DeleteCell(document, b));
        Assert.Same(a, DocumentEditor.DeleteCell(document, c));
        Assert.Null(DocumentEditor.DeleteCell(document, a));
        Assert.Empty(document.Cells);
    }

    [Fact]
    public void InsertElement_GoesAfterTheGivenElement()
    {
        var cell = new Cell();
        var first = new TextElement { Text = "1" };
        var last = new TextElement { Text = "3" };
        cell.Elements.Add(first);
        cell.Elements.Add(last);

        DocumentEditor.InsertElement(cell, new TextElement { Text = "2" }, after: first);
        DocumentEditor.InsertElement(cell, new DividerElement());

        Assert.Equal(["text", "text", "text", "divider"], cell.Elements.Select(e => e.TypeName));
        Assert.Equal("2", ((TextElement)cell.Elements[1]).Text);
    }

    [Fact]
    public void AssetStore_Add_GeneratesUniqueSafePaths()
    {
        var store = new AssetStore();

        Assert.Equal("assets/image-001.png", store.Add([1], "image", ".PNG"));
        Assert.Equal("assets/image-002.png", store.Add([2], "image", "png"));
        Assert.Equal("assets/attachment-001.bin", store.Add([3], "attachment", ".t x/t"));
        Assert.Equal("assets/attachment-002", store.Add([4], "attachment", ""));
    }
}
