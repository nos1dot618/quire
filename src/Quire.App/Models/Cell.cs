using System.Collections.ObjectModel;

namespace Quire.Models;

/// <summary>A named container for an ordered mix of content elements.</summary>
public sealed class Cell : ObservableObject
{
    private string _name = "";

    public string Id { get; init; } = Ids.New();

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value ?? "");
    }

    public ObservableCollection<Element> Elements { get; } = [];

    /// <summary>Copies the cell and its elements with fresh ids. Assets are immutable, so the copy shares them.</summary>
    public Cell Duplicate()
    {
        var copy = new Cell { Name = Name };
        foreach (var element in Elements)
            copy.Elements.Add(element.Duplicate());
        return copy;
    }
}

internal static class Ids
{
    public static string New() => Guid.NewGuid().ToString("N");
}
