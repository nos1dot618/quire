using System.IO;
using System.Text.Json.Nodes;
using Quire.Editing;
using Quire.Models;
using Quire.Storage;
using Quire.ViewModels;

namespace Quire.Tests;

public sealed class UndoManagerTests
{
    private readonly QuireDocument _document = new();
    private readonly Queue<Action> _idle = new();
    private DateTime _now = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
    private readonly UndoManager _undo;
    private readonly ChangeTracker _tracker;

    public UndoManagerTests()
    {
        _document.Cells.Add(new Cell { Name = "First", Elements = { new TextElement { Text = "a" } } });
        _undo = new UndoManager(_document, _idle.Enqueue, () => _now);
        _tracker = new ChangeTracker(_document);
        _tracker.Changed += (_, change) => _undo.OnChanged(change.Source, change.Property, change.IsTyping);
    }

    private void Idle()
    {
        while (_idle.TryDequeue(out var action))
            action();
    }

    private TextElement FirstText => (TextElement)_document.Cells[0].Elements[0];

    [Fact]
    public void UndoAndRedo_StructuralChange()
    {
        DocumentEditor.InsertCell(_document, after: null, "Second");
        Idle();

        Assert.True(_undo.Undo());
        Assert.Single(_document.Cells);

        Assert.True(_undo.Redo());
        Assert.Equal(["First", "Second"], _document.Cells.Select(c => c.Name));
    }

    [Fact]
    public void ChangesInOneUiTurn_AreOneStep()
    {
        var cell = _document.Cells[0];
        DocumentEditor.InsertElements(cell, [new TextElement { Text = "x" }, new DividerElement(), new TextElement { Text = "y" }]);
        Idle();

        _undo.Undo();

        Assert.Single(cell.Elements);
    }

    [Fact]
    public void TypingInOneField_MergesWithinTheWindow()
    {
        var element = FirstText;
        element.Text = "ab"; Idle();
        _now += TimeSpan.FromMilliseconds(400);
        element.Text = "abc"; Idle();
        _now += UndoManager.TypingMergeWindow + TimeSpan.FromSeconds(1);
        element.Text = "abcd"; Idle();

        _undo.Undo();
        Assert.Equal("abc", element.Text);
        _undo.Undo();
        Assert.Equal("a", element.Text);
        Assert.False(_undo.CanUndo);
    }

    [Fact]
    public void Undo_ReusesExistingObjects()
    {
        var cell = _document.Cells[0];
        var element = FirstText;
        element.Text = "changed"; Idle();
        cell.Name = "Renamed"; Idle();

        _undo.Undo();
        _undo.Undo();

        Assert.Same(cell, _document.Cells[0]);
        Assert.Same(element, _document.Cells[0].Elements[0]);
        Assert.Equal(("First", "a"), (cell.Name, element.Text));
    }

    [Fact]
    public void Undo_RestoresDeletedCellAndConvertedElement()
    {
        var cell = _document.Cells[0];
        var original = FirstText;
        DocumentEditor.ConvertElement(cell, original, ElementTypes.Code); Idle();
        DocumentEditor.DeleteCell(_document, cell); Idle();

        _undo.Undo();
        Assert.IsType<CodeElement>(Assert.Single(_document.Cells).Elements[0]);

        _undo.Undo();
        var restored = Assert.IsType<TextElement>(_document.Cells[0].Elements[0]);
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal("a", restored.Text);
    }

    [Fact]
    public void NewEdit_ClearsRedo()
    {
        FirstText.Text = "b"; Idle();
        _undo.Undo();
        Assert.True(_undo.CanRedo);

        _now += TimeSpan.FromMinutes(1);
        FirstText.Text = "c"; Idle();

        Assert.False(_undo.CanRedo);
    }

    [Fact]
    public void Undo_DuringOpenBatch_UndoesThatBatch()
    {
        DocumentEditor.InsertCell(_document, after: null, "Second");
        // No idle yet: the user pressed Ctrl+Z in the same turn.
        _undo.Undo();
        Assert.Single(_document.Cells);
    }

    [Fact]
    public void Undo_RestoresMovedCellsAndElements()
    {
        DocumentEditor.InsertCell(_document, after: null, "Second"); Idle();
        var cell = _document.Cells[0];
        cell.Elements.Add(new DividerElement()); Idle();
        DocumentEditor.MoveDown(_document, cell); Idle();
        cell.Elements.Move(0, 1); Idle();

        _undo.Undo();
        Assert.IsType<TextElement>(cell.Elements[0]);
        _undo.Undo();
        Assert.Equal(["First", "Second"], _document.Cells.Select(c => c.Name));
    }
}

public sealed class RecoveryStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("quire-recovery-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static QuireDocument Doc(string text)
    {
        var document = new QuireDocument();
        document.Cells.Add(new Cell { Elements = { new TextElement { Text = text } } });
        return document;
    }

    [Fact]
    public void DraftFromRunningSession_IsNotAnOrphan()
    {
        using var running = new RecoveryStore(_root);
        running.SaveDraft(Doc("unsaved"), @"C:\docs\notes.quire");

        using var other = new RecoveryStore(_root);
        Assert.Empty(other.FindOrphanedDrafts());
    }

    [Fact]
    public void DraftFromCrashedSession_IsOfferedAndRestorable()
    {
        var crashed = new RecoveryStore(_root);
        crashed.SaveDraft(Doc("unsaved work"), @"C:\docs\notes.quire");
        crashed.SimulateCrash();

        using var next = new RecoveryStore(_root);
        var draft = Assert.Single(next.FindOrphanedDrafts());

        Assert.Equal(@"C:\docs\notes.quire", draft.OriginalPath);
        Assert.Equal("notes.quire", draft.DisplayName);
        var restored = DocumentStore.Load(draft.DocumentPath);
        Assert.Equal("unsaved work", ((TextElement)restored.Cells[0].Elements[0]).Text);

        next.Discard(draft);
        Assert.Empty(next.FindOrphanedDrafts());
    }

    [Fact]
    public void CleanExit_AndClearedDrafts_LeaveNothing()
    {
        var clean = new RecoveryStore(_root);
        clean.SaveDraft(Doc("x"), null);
        clean.Dispose();

        var saved = new RecoveryStore(_root);
        saved.SaveDraft(Doc("y"), null);
        saved.ClearDraft();
        saved.SimulateCrash();

        using var next = new RecoveryStore(_root);
        Assert.Empty(next.FindOrphanedDrafts());
        Assert.Single(Directory.GetDirectories(_root)); // only the running session's folder
    }

    [Fact]
    public void Orphans_AreNewestFirst()
    {
        var older = new RecoveryStore(_root);
        older.SaveDraft(Doc("old"), @"C:\a.quire");
        older.SimulateCrash();
        Thread.Sleep(20);
        var newer = new RecoveryStore(_root);
        newer.SaveDraft(Doc("new"), @"C:\b.quire");
        newer.SimulateCrash();

        using var next = new RecoveryStore(_root);
        Assert.Equal(["b.quire", "a.quire"], next.FindOrphanedDrafts().Select(d => d.DisplayName));
    }
}

public sealed class ValidationTests
{
    [Fact]
    public void DuplicateAndMissingIds_AreReplaced()
    {
        var document = DocumentSerializer.FromJson("""
            {"formatVersion":1,"cells":[
              {"id":"same","elements":[{"id":"e","type":"text"},{"id":"e","type":"text"},{"type":"divider"}]},
              {"id":"same","elements":[{"id":"same","type":"hologram"}]}]}
            """);

        var ids = document.Cells.Select(c => c.Id)
            .Concat(document.Cells.SelectMany(c => c.Elements).Select(e => e.Id))
            .ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.False(string.IsNullOrEmpty(id)));
        Assert.Equal("same", document.Cells[0].Id);
        Assert.Equal("e", document.Cells[0].Elements[0].Id);

        // The unknown element's preserved JSON carries its new id, so saving stays consistent.
        var unknown = Assert.IsType<UnknownElement>(document.Cells[1].Elements[0]);
        Assert.Equal(unknown.Id, unknown.Raw["id"]!.GetValue<string>());
    }

    [Fact]
    public void Migrate_AppliesEachStepInOrder()
    {
        var root = new JsonObject { ["formatVersion"] = 1, ["log"] = "" };
        Func<JsonObject, JsonObject>[] migrations =
        [
            json => { json["log"] = json["log"]!.GetValue<string>() + "1>2;"; return json; },
            json => { json["log"] = json["log"]!.GetValue<string>() + "2>3;"; return json; },
        ];

        var fromOne = DocumentSerializer.Migrate((JsonObject)root.DeepClone(), 1, migrations);
        var fromTwo = DocumentSerializer.Migrate(new JsonObject { ["formatVersion"] = 2, ["log"] = "" }, 2, migrations);

        Assert.Equal(("1>2;2>3;", 3), (fromOne["log"]!.GetValue<string>(), fromOne["formatVersion"]!.GetValue<int>()));
        Assert.Equal(("2>3;", 3), (fromTwo["log"]!.GetValue<string>(), fromTwo["formatVersion"]!.GetValue<int>()));
    }
}
