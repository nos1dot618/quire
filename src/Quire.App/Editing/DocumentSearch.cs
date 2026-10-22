using Quire.Models;

namespace Quire.Editing;

/// <summary>Where a match was found: a cell's name (<see cref="Element"/> is null) or one text field of an element.</summary>
public sealed record SearchMatch(Cell Cell, Element? Element, string Field, int Index, int Length);

/// <summary>Case-insensitive plain-text search over cell names and every editable text field, in document order.</summary>
public static class DocumentSearch
{
    public const string NameField = nameof(Cell.Name);

    public static IReadOnlyList<SearchMatch> Find(QuireDocument document, string query)
    {
        var matches = new List<SearchMatch>();
        if (string.IsNullOrEmpty(query))
            return matches;

        foreach (var cell in document.Cells)
        {
            AddMatches(matches, cell, null, NameField, cell.Name, query);
            foreach (var element in cell.Elements)
            {
                foreach (var (field, text) in SearchableFields(element))
                    AddMatches(matches, cell, element, field, text, query);
            }
        }
        return matches;
    }

    /// <summary>The text fields of an element in on-screen order, named after their model properties.</summary>
    public static IEnumerable<(string Field, string Text)> SearchableFields(Element element)
    {
        switch (element)
        {
            case CodeElement code:
                yield return (nameof(CodeElement.Language), code.Language);
                yield return (nameof(CodeElement.Text), code.Text);
                break;
            case TextualElement textual:
                yield return (nameof(TextualElement.Text), textual.Text);
                break;
            case LinkElement link:
                yield return (nameof(LinkElement.Url), link.Url);
                yield return (nameof(LinkElement.Title), link.Title);
                break;
            case FileReferenceElement reference:
                yield return (nameof(FileReferenceElement.Path), reference.Path);
                break;
            case AttachmentElement attachment:
                yield return (nameof(AttachmentElement.FileName), attachment.FileName);
                break;
        }
    }

    private static void AddMatches(List<SearchMatch> matches, Cell cell, Element? element, string field, string text, string query)
    {
        for (var index = text.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
             index >= 0;
             index = text.IndexOf(query, index + Math.Max(1, query.Length), StringComparison.CurrentCultureIgnoreCase))
        {
            matches.Add(new SearchMatch(cell, element, field, index, query.Length));
        }
    }
}
