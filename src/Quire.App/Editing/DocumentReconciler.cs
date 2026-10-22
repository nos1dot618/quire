using System.Collections.ObjectModel;
using Quire.Models;

namespace Quire.Editing;

/// <summary>
/// Makes a live document match another state (for undo/redo) while reusing existing cell and
/// element objects wherever ids and types match. Reusing objects keeps their editors on screen,
/// so restoring an older state does not rebuild the whole page.
/// </summary>
public static class DocumentReconciler
{
    public static void Apply(QuireDocument target, QuireDocument state)
    {
        target.Title = state.Title;

        var existingCells = ById(target.Cells, cell => cell.Id);
        var desiredCells = new List<Cell>(state.Cells.Count);
        foreach (var stateCell in state.Cells)
        {
            if (existingCells.TryGetValue(stateCell.Id, out var cell))
            {
                cell.Name = stateCell.Name;
                ApplyElements(cell, stateCell);
                desiredCells.Add(cell);
            }
            else
            {
                desiredCells.Add(stateCell);
            }
        }
        Sync(target.Cells, desiredCells);
    }

    private static void ApplyElements(Cell target, Cell state)
    {
        var existing = ById(target.Elements, element => element.Id);
        var desired = new List<Element>(state.Elements.Count);
        foreach (var stateElement in state.Elements)
        {
            if (existing.TryGetValue(stateElement.Id, out var element) && TryCopy(stateElement, element))
                desired.Add(element);
            else
                desired.Add(stateElement);
        }
        Sync(target.Elements, desired);
    }

    /// <summary>Copies editable content between two elements of the same type. Returns false if the types differ.</summary>
    private static bool TryCopy(Element from, Element to)
    {
        switch (from, to)
        {
            case (CodeElement source, CodeElement target):
                target.Language = source.Language;
                target.Text = source.Text;
                return true;
            case (TextualElement source, TextualElement target) when source.GetType() == target.GetType():
                target.Text = source.Text;
                return true;
            case (LinkElement source, LinkElement target):
                target.Url = source.Url;
                target.Title = source.Title;
                return true;
            case (AttachmentElement source, AttachmentElement target) when source.FileName == target.FileName:
                target.Asset = source.Asset;
                return true;
            case (AssetElement source, AssetElement target) when source.GetType() == target.GetType() && source is not AttachmentElement:
                target.Asset = source.Asset;
                return true;
            case (FileReferenceElement source, FileReferenceElement target):
                target.Path = source.Path;
                return true;
            case (DividerElement, DividerElement):
                return true;
            default:
                return false;
        }
    }

    /// <summary>Indexes by id. Ids are unique after loading, but a duplicate must not break undo, so the first wins.</summary>
    private static Dictionary<string, T> ById<T>(IEnumerable<T> items, Func<T, string> id)
    {
        var result = new Dictionary<string, T>();
        foreach (var item in items)
            result.TryAdd(id(item), item);
        return result;
    }

    /// <summary>Edits a collection into the desired order with the fewest inserts, moves and removals.</summary>
    private static void Sync<T>(ObservableCollection<T> collection, IReadOnlyList<T> desired) where T : class
    {
        var keep = new HashSet<T>(desired, ReferenceEqualityComparer.Instance);
        for (var i = collection.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(collection[i]))
                collection.RemoveAt(i);
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var index = IndexOf(collection, desired[i]);
            if (index < 0)
                collection.Insert(i, desired[i]);
            else if (index != i)
                collection.Move(index, i);
        }
    }

    private static int IndexOf<T>(ObservableCollection<T> collection, T item) where T : class
    {
        for (var i = 0; i < collection.Count; i++)
        {
            if (ReferenceEquals(collection[i], item))
                return i;
        }
        return -1;
    }
}
