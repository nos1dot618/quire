using System.Collections.Specialized;
using System.ComponentModel;
using Quire.Models;

namespace Quire.ViewModels;

/// <summary>One edit: a property change on <see cref="Source"/>, or a collection change when <see cref="Property"/> is null.</summary>
public sealed record DocumentChange(object Source, string? Property)
{
    /// <summary>Text-like edits (typing) that undo may merge into a single step.</summary>
    public bool IsTyping => Property is not null && Source is not QuireDocument
        && Property is nameof(TextualElement.Text) or nameof(Cell.Name) or nameof(CodeElement.Language)
            or nameof(LinkElement.Url) or nameof(LinkElement.Title) or nameof(FileReferenceElement.Path);
}

/// <summary>Raises <see cref="Changed"/> whenever anything in a document is edited: titles, cells, or elements.</summary>
public sealed class ChangeTracker : IDisposable
{
    private readonly QuireDocument _document;
    private readonly HashSet<INotifyPropertyChanged> _watchedItems = [];
    private readonly HashSet<INotifyCollectionChanged> _watchedCollections = [];

    public event EventHandler<DocumentChange>? Changed;

    public ChangeTracker(QuireDocument document)
    {
        _document = document;
        Watch(document);
        WatchCollection(document.Cells);
        foreach (var cell in document.Cells)
            WatchCell(cell);
    }

    public void Dispose()
    {
        foreach (var item in _watchedItems)
            item.PropertyChanged -= OnPropertyChanged;
        foreach (var collection in _watchedCollections)
            collection.CollectionChanged -= OnCollectionChanged;
        _watchedItems.Clear();
        _watchedCollections.Clear();
    }

    private void WatchCell(Cell cell)
    {
        Watch(cell);
        WatchCollection(cell.Elements);
        foreach (var element in cell.Elements)
            Watch(element);
    }

    private void Watch(INotifyPropertyChanged item)
    {
        if (_watchedItems.Add(item))
            item.PropertyChanged += OnPropertyChanged;
    }

    private void WatchCollection(INotifyCollectionChanged collection)
    {
        if (_watchedCollections.Add(collection))
            collection.CollectionChanged += OnCollectionChanged;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        Changed?.Invoke(this, new DocumentChange(sender!, e.PropertyName));

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Removed items are left subscribed; they no longer raise events that matter and are
        // released with the tracker when the document closes.
        foreach (var item in e.NewItems?.OfType<object>() ?? [])
        {
            if (item is Cell cell)
                WatchCell(cell);
            else if (item is Element element)
                Watch(element);
        }
        Changed?.Invoke(this, new DocumentChange(sender!, Property: null));
    }
}
