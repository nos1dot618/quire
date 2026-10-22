using Quire.Models;

namespace Quire.Editing;

/// <summary>Structural edits on a document's cells. Kept free of UI types so it can be tested directly.</summary>
public static class DocumentEditor
{
    /// <summary>Inserts a new cell after <paramref name="after"/>, or at the end when it is null.</summary>
    public static Cell InsertCell(QuireDocument document, Cell? after, string name = "")
    {
        var cell = new Cell { Name = name };
        cell.Elements.Add(new TextElement());
        document.Cells.Insert(IndexAfter(document, after), cell);
        return cell;
    }

    public static Cell DuplicateCell(QuireDocument document, Cell cell)
    {
        var copy = cell.Duplicate();
        document.Cells.Insert(IndexAfter(document, cell), copy);
        return copy;
    }

    public static bool CanMoveUp(QuireDocument document, Cell? cell) =>
        cell is not null && document.Cells.IndexOf(cell) > 0;

    public static bool CanMoveDown(QuireDocument document, Cell? cell) =>
        cell is not null && document.Cells.IndexOf(cell) is var index && index >= 0 && index < document.Cells.Count - 1;

    public static void MoveUp(QuireDocument document, Cell cell)
    {
        if (CanMoveUp(document, cell))
        {
            var index = document.Cells.IndexOf(cell);
            document.Cells.Move(index, index - 1);
        }
    }

    public static void MoveDown(QuireDocument document, Cell cell)
    {
        if (CanMoveDown(document, cell))
        {
            var index = document.Cells.IndexOf(cell);
            document.Cells.Move(index, index + 1);
        }
    }

    /// <summary>Deletes a cell and returns the cell that should be selected next, if any.</summary>
    public static Cell? DeleteCell(QuireDocument document, Cell cell)
    {
        var index = document.Cells.IndexOf(cell);
        if (index < 0)
            return null;

        document.Cells.RemoveAt(index);
        if (document.Cells.Count == 0)
            return null;
        return document.Cells[Math.Min(index, document.Cells.Count - 1)];
    }

    /// <summary>Adds an element to a cell after <paramref name="after"/>, or at the end when it is null or not in the cell.</summary>
    public static void InsertElement(Cell cell, Element element, Element? after = null)
    {
        var index = after is null ? -1 : cell.Elements.IndexOf(after);
        cell.Elements.Insert(index < 0 ? cell.Elements.Count : index + 1, element);
    }

    /// <summary>Inserts several elements in order after <paramref name="after"/>, or at the end.</summary>
    public static void InsertElements(Cell cell, IEnumerable<Element> elements, Element? after = null)
    {
        foreach (var element in elements)
        {
            InsertElement(cell, element, after);
            after = element;
        }
    }

    public static bool CanConvert(Element element, string targetType) =>
        element is TextualElement && element.TypeName != targetType
        && targetType is ElementTypes.Text or ElementTypes.Markdown or ElementTypes.Code;

    /// <summary>
    /// Converts a text, Markdown or code element to another of those types in place, keeping its id,
    /// position and text. Returns the new element, or null when the conversion is not possible.
    /// </summary>
    public static Element? ConvertElement(Cell cell, Element element, string targetType)
    {
        var index = cell.Elements.IndexOf(element);
        if (index < 0 || !CanConvert(element, targetType))
            return null;

        var text = ((TextualElement)element).Text;
        Element converted = targetType switch
        {
            ElementTypes.Markdown => new MarkdownElement { Id = element.Id, Text = text },
            ElementTypes.Code => new CodeElement { Id = element.Id, Text = text },
            _ => new TextElement { Id = element.Id, Text = text },
        };
        cell.Elements[index] = converted;
        return converted;
    }

    public static Cell? FindCell(QuireDocument document, Element element) =>
        document.Cells.FirstOrDefault(cell => cell.Elements.Contains(element));

    private static int IndexAfter(QuireDocument document, Cell? after)
    {
        var index = after is null ? -1 : document.Cells.IndexOf(after);
        return index < 0 ? document.Cells.Count : index + 1;
    }
}
