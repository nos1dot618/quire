using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Quire.Editing;
using Quire.Export;
using Quire.Models;
using Quire.Storage;

namespace Quire.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const string ImageFilter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.tif;*.tiff;*.ico|All files|*.*";

    private readonly IDialogService _dialogs;
    private QuireDocument _document = null!;
    private readonly Action<Action> _whenIdle;
    private readonly RecoveryStore? _recovery;
    private ChangeTracker? _tracker;
    private UndoManager _undo = null!;
    private string _savedState = "";
    private bool _draftStale;
    private string? _filePath;
    private bool _isDirty;
    private Cell? _selectedCell;
    private Element? _selectedElement;
    private bool _isPreviewMode = true;
    private FileInsertMode _fileInsertMode = FileInsertMode.Reference;

    /// <param name="whenIdle">Runs an action after the current burst of UI changes; groups edits into undo steps.</param>
    /// <param name="recovery">Where unsaved work is kept for crash recovery, or null to disable it.</param>
    private readonly SettingsStore? _settingsStore;
    private readonly Settings _settings;

    public MainViewModel(IDialogService dialogs, Action<Action> whenIdle, RecoveryStore? recovery = null, SettingsStore? settingsStore = null)
    {
        _dialogs = dialogs;
        _whenIdle = whenIdle;
        _recovery = recovery;
        _settingsStore = settingsStore;
        _settings = settingsStore?.Load() ?? new Settings();
        _isPreviewMode = _settings.PreviewMode;
        _fileInsertMode = _settings.AttachDroppedFiles ? FileInsertMode.Attach : FileInsertMode.Reference;
        RecentDocuments = new ObservableCollection<string>(_settings.RecentDocuments);

        OpenRecentCommand = new RelayCommand<string>(OpenRecent);
        ClearRecentCommand = new RelayCommand(ClearRecent, () => RecentDocuments.Count > 0);
        ExportCommand = new RelayCommand<object?>(parameter =>
        {
            if (parameter is ExportFormat format || Enum.TryParse(parameter as string, out format))
                Export(format);
        });

        UndoCommand = new RelayCommand(() => _undo.Undo(), () => _undo.CanUndo);
        RedoCommand = new RelayCommand(() => _undo.Redo(), () => _undo.CanRedo);

        NewCommand = new RelayCommand(New);
        OpenCommand = new RelayCommand(Open);
        SaveCommand = new RelayCommand(() => Save());
        SaveAsCommand = new RelayCommand(() => SaveAs());

        InsertCellCommand = new RelayCommand(InsertCell);
        InsertTextCommand = new RelayCommand(() => InsertElement(new TextElement()));
        InsertMarkdownCommand = new RelayCommand(() => InsertElement(new MarkdownElement()));
        InsertCodeCommand = new RelayCommand(() => InsertElement(new CodeElement()));
        InsertLinkCommand = new RelayCommand(() => InsertElement(new LinkElement()));
        InsertDividerCommand = new RelayCommand(() => InsertElement(new DividerElement()));
        InsertImageCommand = new RelayCommand(InsertImage);
        InsertFileReferenceCommand = new RelayCommand(InsertFileReference);
        InsertAttachmentCommand = new RelayCommand(InsertAttachment);

        RenameCellCommand = new RelayCommand(() => RenameRequested?.Invoke(this, SelectedCell!), () => SelectedCell is not null);
        DuplicateCellCommand = new RelayCommand(DuplicateCell, () => SelectedCell is not null);
        MoveCellUpCommand = new RelayCommand(() => DocumentEditor.MoveUp(Document, SelectedCell!), () => DocumentEditor.CanMoveUp(Document, SelectedCell));
        MoveCellDownCommand = new RelayCommand(() => DocumentEditor.MoveDown(Document, SelectedCell!), () => DocumentEditor.CanMoveDown(Document, SelectedCell));
        DeleteCellCommand = new RelayCommand(DeleteCell, () => SelectedCell is not null);

        // Element commands act on the element passed as the parameter (context menus),
        // or on the element that has focus (main menu and keyboard shortcuts).
        DeleteElementCommand = ElementCommand(DeleteElement);
        MoveElementUpCommand = ElementCommand(element => MoveElement(element, -1), element => CanMoveElement(element, -1));
        MoveElementDownCommand = ElementCommand(element => MoveElement(element, +1), element => CanMoveElement(element, +1));
        ConvertToTextCommand = ConvertCommand(ElementTypes.Text);
        ConvertToMarkdownCommand = ConvertCommand(ElementTypes.Markdown);
        ConvertToCodeCommand = ConvertCommand(ElementTypes.Code);
        ReplaceImageCommand = ElementCommand(ReplaceImage, element => element is ImageElement or GifElement);

        SetDocument(QuireDocument.CreateNew(), filePath: null);
    }

    /// <summary>Raised when the view should put the cursor in a cell's name box.</summary>
    public event EventHandler<Cell>? RenameRequested;

    /// <summary>Raised when the view should move keyboard focus to a newly inserted element.</summary>
    public event EventHandler<Element>? FocusElementRequested;

    public QuireDocument Document
    {
        get => _document;
        private set => SetField(ref _document, value);
    }

    public string? FilePath
    {
        get => _filePath;
        private set
        {
            if (SetField(ref _filePath, value))
                OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetField(ref _isDirty, value))
                OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public Cell? SelectedCell
    {
        get => _selectedCell;
        set
        {
            if (SetField(ref _selectedCell, value) && SelectedElement is not null && value?.Elements.Contains(SelectedElement) != true)
                SelectedElement = null;
        }
    }

    /// <summary>The element that last had keyboard focus. New elements are inserted after it.</summary>
    public Element? SelectedElement
    {
        get => _selectedElement;
        set => SetField(ref _selectedElement, value);
    }

    /// <summary>When on, Markdown and code show rendered output until clicked. When off, they always show source.</summary>
    public bool IsPreviewMode
    {
        get => _isPreviewMode;
        set
        {
            if (SetField(ref _isPreviewMode, value))
                SaveSettings(s => s.PreviewMode = value);
        }
    }

    public ThemeChoice Theme
    {
        get => _settings.Theme;
        set
        {
            if (_settings.Theme == value)
                return;
            SaveSettings(s => s.Theme = value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSystemTheme));
            OnPropertyChanged(nameof(IsLightTheme));
            OnPropertyChanged(nameof(IsDarkTheme));
        }
    }

    // Checkable menu items bind to these; checking one selects that theme.
    public bool IsSystemTheme { get => Theme == ThemeChoice.System; set { if (value) Theme = ThemeChoice.System; } }
    public bool IsLightTheme { get => Theme == ThemeChoice.Light; set { if (value) Theme = ThemeChoice.Light; } }
    public bool IsDarkTheme { get => Theme == ThemeChoice.Dark; set { if (value) Theme = ThemeChoice.Dark; } }

    /// <summary>Whether exports write cell names as headings.</summary>
    public bool ExportCellNames
    {
        get => _settings.ExportCellNames;
        set
        {
            if (_settings.ExportCellNames == value)
                return;
            SaveSettings(s => s.ExportCellNames = value);
            OnPropertyChanged();
        }
    }

    /// <summary>Most recently opened or saved documents, newest first.</summary>
    public ObservableCollection<string> RecentDocuments { get; }

    public bool HasRecentDocuments => RecentDocuments.Count > 0;

    /// <summary>Whether dropped and pasted non-image files are attached (copied in) rather than referenced.</summary>
    public bool AttachDroppedFiles
    {
        get => _fileInsertMode == FileInsertMode.Attach;
        set
        {
            if (SetField(ref _fileInsertMode, value ? FileInsertMode.Attach : FileInsertMode.Reference, nameof(FileInsertMode)))
            {
                SaveSettings(s => s.AttachDroppedFiles = value);
                OnPropertyChanged(nameof(AttachDroppedFiles));
                OnPropertyChanged(nameof(ReferenceDroppedFiles));
            }
        }
    }

    public bool ReferenceDroppedFiles
    {
        get => !AttachDroppedFiles;
        set => AttachDroppedFiles = !value;
    }

    public FileInsertMode FileInsertMode => _fileInsertMode;

    public string DocumentName => FilePath is null ? "Untitled" : Path.GetFileNameWithoutExtension(FilePath);

    public string WindowTitle => $"{(IsDirty ? "*" : "")}{DocumentName} - Quire";

    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand NewCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand InsertCellCommand { get; }
    public ICommand InsertTextCommand { get; }
    public ICommand InsertMarkdownCommand { get; }
    public ICommand InsertCodeCommand { get; }
    public ICommand InsertLinkCommand { get; }
    public ICommand InsertDividerCommand { get; }
    public ICommand InsertImageCommand { get; }
    public ICommand InsertFileReferenceCommand { get; }
    public ICommand InsertAttachmentCommand { get; }
    public ICommand RenameCellCommand { get; }
    public ICommand DuplicateCellCommand { get; }
    public ICommand MoveCellUpCommand { get; }
    public ICommand MoveCellDownCommand { get; }
    public ICommand DeleteCellCommand { get; }
    public ICommand DeleteElementCommand { get; }
    public ICommand MoveElementUpCommand { get; }
    public ICommand MoveElementDownCommand { get; }
    public ICommand ConvertToTextCommand { get; }
    public ICommand ConvertToMarkdownCommand { get; }
    public ICommand ConvertToCodeCommand { get; }
    public ICommand ReplaceImageCommand { get; }
    public ICommand OpenRecentCommand { get; }
    public ICommand ClearRecentCommand { get; }
    public ICommand ExportCommand { get; }

    /// <summary>Asks to save unsaved changes. Returns false if the user cancelled.</summary>
    public bool ConfirmClose()
    {
        if (!IsDirty)
            return true;

        return _dialogs.AskSaveChanges(DocumentName) switch
        {
            SaveChangesChoice.Save => Save(),
            SaveChangesChoice.Discard => DiscardChanges(),
            _ => false,
        };
    }

    public void OpenPath(string path)
    {
        if (!ConfirmClose())
            return;

        QuireDocument document;
        try
        {
            document = DocumentStore.Load(path);
        }
        catch (QuireFormatException ex)
        {
            _dialogs.ShowError($"Quire could not open \"{Path.GetFileName(path)}\".\n\n{ex.Message}");
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError($"Quire could not read \"{Path.GetFileName(path)}\".\n\n{ex.Message}");
            return;
        }

        SetDocument(document, path);
        AddRecent(path);

        if (document.MissingAssets.Count > 0)
            _dialogs.ShowWarning(
                $"{document.MissingAssets.Count} embedded file(s) are missing from this document and are shown as placeholders.");
    }

    /// <summary>
    /// Adds pasted or dropped content to <paramref name="cell"/> after <paramref name="after"/>
    /// (or to the selected cell after the selected element). Content keeps its original order.
    /// </summary>
    public void InsertContent(IReadOnlyList<Element> elements, Cell? cell = null, Element? after = null)
    {
        if (elements.Count == 0)
            return;

        cell ??= TargetCell();
        if (after is null && cell == SelectedCell)
            after = SelectedElement;

        DocumentEditor.InsertElements(cell, elements, after);
        SelectedCell = cell;
        SelectedElement = elements[^1];
    }

    /// <summary>Creates elements for dropped or pasted files, reporting files that cannot be read.</summary>
    public IReadOnlyList<Element> ElementsFromFiles(IEnumerable<string> paths, FileInsertMode mode)
    {
        try
        {
            return ContentRecognizer.FromFiles(Document, paths, mode);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError($"Quire could not read a dropped file.\n\n{ex.Message}");
            return [];
        }
    }

    private void New()
    {
        if (ConfirmClose())
            SetDocument(QuireDocument.CreateNew(), filePath: null);
    }

    private void Open()
    {
        if (_dialogs.ShowOpenDocument() is { } path)
            OpenPath(path);
    }

    private bool Save() => FilePath is null ? SaveAs() : SaveTo(FilePath);

    private bool SaveAs()
    {
        var path = _dialogs.ShowSaveDocument(DocumentName + DocumentStore.FileExtension);
        return path is not null && SaveTo(path);
    }

    private bool SaveTo(string path)
    {
        try
        {
            DocumentStore.Save(Document, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError($"Quire could not save \"{Path.GetFileName(path)}\". The previous version on disk is unchanged.\n\n{ex.Message}");
            return false;
        }

        FilePath = path;
        AddRecent(path);
        OnPropertyChanged(nameof(DocumentName));
        _savedState = _undo.CurrentState;
        IsDirty = false;
        _draftStale = false;
        _recovery?.ClearDraft();
        return true;
    }

    private bool DiscardChanges()
    {
        _recovery?.ClearDraft();
        return true;
    }

    /// <summary>
    /// Writes a recovery draft if anything changed since the last one. Called on a timer; failures
    /// are ignored because the draft is only a safety net and must never interrupt editing.
    /// </summary>
    public void SaveDraftIfNeeded()
    {
        if (_recovery is null || !_draftStale)
            return;

        try
        {
            if (IsDirty)
                _recovery.SaveDraft(Document, FilePath);
            else
                _recovery.ClearDraft();
            _draftStale = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Opens an orphaned draft as the current document, still unsaved, then removes the orphan.</summary>
    public void RestoreDraft(RecoveryDraft draft)
    {
        QuireDocument document;
        try
        {
            document = DocumentStore.Load(draft.DocumentPath);
        }
        catch (Exception ex) when (ex is QuireFormatException or IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError($"Quire could not recover \"{draft.DisplayName}\".\n\n{ex.Message}");
            return;
        }

        SetDocument(document, draft.OriginalPath);
        // The recovered content has not been saved to the original file.
        _savedState = "";
        IsDirty = true;
        _draftStale = true;
        SaveDraftIfNeeded();
        _recovery?.Discard(draft);
    }

    private void SetDocument(QuireDocument document, string? filePath)
    {
        _tracker?.Dispose();
        if (_undo is not null)
            _undo.Restored -= OnUndoRestored;
        Document = document;
        FilePath = filePath;
        OnPropertyChanged(nameof(DocumentName));
        SelectedCell = document.Cells.FirstOrDefault();
        SelectedElement = null;
        IsDirty = false;
        _draftStale = true;

        _undo = new UndoManager(document, _whenIdle);
        _undo.Restored += OnUndoRestored;
        _savedState = _undo.CurrentState;

        _tracker = new ChangeTracker(document);
        _tracker.Changed += (_, change) =>
        {
            _undo.OnChanged(change.Source, change.Property, change.IsTyping);
            IsDirty = true;
            _draftStale = true;
        };
    }

    /// <summary>Raised after undo or redo changes the document.</summary>
    public event EventHandler? Restored;

    private void OnUndoRestored(object? sender, EventArgs e)
    {
        IsDirty = _undo.CurrentState != _savedState;
        _draftStale = true;
        if (SelectedCell is not null && !Document.Cells.Contains(SelectedCell))
            SelectedCell = Document.Cells.FirstOrDefault();
        if (SelectedElement is not null && SelectedCell?.Elements.Contains(SelectedElement) != true)
            SelectedElement = null;
        Restored?.Invoke(this, EventArgs.Empty);
    }

    private void SaveSettings(Action<Settings> change)
    {
        change(_settings);
        _settingsStore?.Save(_settings);
    }

    private void AddRecent(string path)
    {
        _settings.AddRecent(path);
        _settingsStore?.Save(_settings);
        RecentDocuments.Clear();
        foreach (var recent in _settings.RecentDocuments)
            RecentDocuments.Add(recent);
        OnPropertyChanged(nameof(HasRecentDocuments));
    }

    private void OpenRecent(string path)
    {
        if (File.Exists(path))
        {
            OpenPath(path);
            return;
        }

        _dialogs.ShowWarning($"\"{Path.GetFileName(path)}\" was not found and has been removed from Recent Documents.\n\n{path}");
        _settings.RecentDocuments.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _settingsStore?.Save(_settings);
        RecentDocuments.Remove(path);
        OnPropertyChanged(nameof(HasRecentDocuments));
    }

    private void ClearRecent()
    {
        SaveSettings(s => s.RecentDocuments.Clear());
        RecentDocuments.Clear();
        OnPropertyChanged(nameof(HasRecentDocuments));
    }

    private void Export(ExportFormat format)
    {
        var suggested = DocumentName + Exporter.Extension(format);
        if (_dialogs.ShowSaveFile($"Export as {format}", Exporter.Filter(format), suggested) is not { } path)
            return;

        try
        {
            Exporter.Export(Document, path, format, new ExportOptions { IncludeCellNames = ExportCellNames });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            _dialogs.ShowError($"Quire could not export \"{Path.GetFileName(path)}\".\n\n{ex.Message}");
        }
    }

    private void InsertCell()
    {
        SelectedCell = DocumentEditor.InsertCell(Document, SelectedCell);
        RenameRequested?.Invoke(this, SelectedCell);
    }

    private void DuplicateCell() => SelectedCell = DocumentEditor.DuplicateCell(Document, SelectedCell!);

    private void DeleteCell() => SelectedCell = DocumentEditor.DeleteCell(Document, SelectedCell!);

    private Cell TargetCell() =>
        SelectedCell ??= Document.Cells.LastOrDefault() ?? DocumentEditor.InsertCell(Document, after: null);

    /// <summary>Adds an element after the focused element and moves focus to it.</summary>
    private void InsertElement(Element element)
    {
        InsertContent([element]);
        FocusElementRequested?.Invoke(this, element);
    }

    private void InsertImage()
    {
        if (_dialogs.ShowOpenFile("Insert Image", ImageFilter) is not { } path || ReadFile(path) is not { } data)
            return;

        InsertElement(ContentRecognizer.FromImage(Document, data, Path.GetExtension(path)));
    }

    private void InsertFileReference()
    {
        if (_dialogs.ShowOpenFile("Insert File Reference", "All files|*.*") is { } path)
            InsertElement(new FileReferenceElement { Path = path });
    }

    private void InsertAttachment()
    {
        if (_dialogs.ShowOpenFile("Attach File", "All files|*.*") is not { } path || ReadFile(path) is not { } data)
            return;

        var asset = Document.Assets.Add(data, "attachment", Path.GetExtension(path));
        InsertElement(new AttachmentElement { Asset = asset, FileName = Path.GetFileName(path) });
    }

    private void ReplaceImage(Element element)
    {
        if (element is not AssetElement asset
            || _dialogs.ShowOpenFile("Replace Image", ImageFilter) is not { } path
            || ReadFile(path) is not { } data)
        {
            return;
        }

        var replacement = ContentRecognizer.FromImage(Document, data, Path.GetExtension(path));
        if (replacement.GetType() == element.GetType())
        {
            // Same kind: keep the element and its identity, just point it at the new picture.
            asset.Asset = replacement.Asset;
        }
        else if (DocumentEditor.FindCell(Document, element) is { } cell)
        {
            // A still image replaced by a GIF (or the reverse) changes element type but keeps id and position.
            Element swapped = replacement is GifElement
                ? new GifElement { Id = element.Id, Asset = replacement.Asset }
                : new ImageElement { Id = element.Id, Asset = replacement.Asset };
            cell.Elements[cell.Elements.IndexOf(element)] = swapped;
        }
    }

    private void DeleteElement(Element element)
    {
        if (DocumentEditor.FindCell(Document, element) is not { } cell)
            return;

        var index = cell.Elements.IndexOf(element);
        cell.Elements.RemoveAt(index);
        if (SelectedElement == element)
            SelectedElement = cell.Elements.Count == 0 ? null : cell.Elements[Math.Max(0, index - 1)];
    }

    private bool CanMoveElement(Element element, int offset)
    {
        var cell = DocumentEditor.FindCell(Document, element);
        var target = cell is null ? -1 : cell.Elements.IndexOf(element) + offset;
        return cell is not null && target >= 0 && target < cell.Elements.Count;
    }

    private void MoveElement(Element element, int offset)
    {
        if (!CanMoveElement(element, offset))
            return;

        var cell = DocumentEditor.FindCell(Document, element)!;
        var index = cell.Elements.IndexOf(element);
        cell.Elements.Move(index, index + offset);
        FocusElementRequested?.Invoke(this, element);
    }

    private ICommand ConvertCommand(string targetType) => ElementCommand(
        element =>
        {
            if (DocumentEditor.FindCell(Document, element) is { } cell
                && DocumentEditor.ConvertElement(cell, element, targetType) is { } converted)
            {
                SelectedElement = converted;
                FocusElementRequested?.Invoke(this, converted);
            }
        },
        element => DocumentEditor.CanConvert(element, targetType));

    private RelayCommand<object?> ElementCommand(Action<Element> execute, Func<Element, bool>? canExecute = null) =>
        new(
            parameter => { if (Resolve(parameter) is { } element) execute(element); },
            parameter => Resolve(parameter) is { } element && (canExecute?.Invoke(element) ?? true));

    private Element? Resolve(object? parameter) => parameter as Element ?? SelectedElement;

    private byte[]? ReadFile(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError($"Quire could not read \"{Path.GetFileName(path)}\".\n\n{ex.Message}");
            return null;
        }
    }
}
