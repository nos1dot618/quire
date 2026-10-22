using System.Collections.ObjectModel;

namespace Quire.Models;

/// <summary>A Quire document: an ordered collection of cells plus the binary assets they reference.</summary>
public sealed class QuireDocument : ObservableObject
{
    private string _title = "Untitled";

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public ObservableCollection<Cell> Cells { get; } = [];

    public AssetStore Assets { get; } = new();

    /// <summary>Asset paths that elements reference but the archive did not contain when it was loaded.</summary>
    public List<string> MissingAssets { get; } = [];

    public static QuireDocument CreateNew()
    {
        var document = new QuireDocument();
        document.Cells.Add(new Cell { Elements = { new TextElement() } });
        return document;
    }
}
