using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Quire.Editing;
using Quire.Models;

namespace Quire.UI;

/// <summary>Editing interactions: selection and focus, paste, drag-and-drop, and per-element actions.</summary>
public partial class MainWindow
{
    // --- Selection and focus --------------------------------------------------

    /// <summary>Tracks the cell and element that receive focus, so menu commands act on them.</summary>
    private void Cells_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is DependencyObject target)
            Select(target);
    }

    /// <summary>Clicking an image (which cannot take focus) still selects it and its cell.</summary>
    private void Cells_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject target)
            Select(target);
    }

    private void Select(DependencyObject target)
    {
        var (cell, element) = FindTarget(target);
        if (cell is null)
            return;
        ViewModel.SelectedCell = cell;
        if (element is not null)
            ViewModel.SelectedElement = element;
    }

    /// <summary>Finds the cell and element that contain a visual or text element in the document.</summary>
    private static (Cell? Cell, Element? Element) FindTarget(DependencyObject target)
    {
        Element? element = null;
        for (var current = target; current is not null; current = ParentOf(current))
        {
            var context = current switch
            {
                FrameworkElement fe => fe.DataContext,
                FrameworkContentElement fce => fce.DataContext,
                _ => null,
            };
            element ??= context as Element;
            if (current is ListBoxItem { DataContext: Cell cell })
                return (cell, element);
        }
        return (null, null);
    }

    private static DependencyObject? ParentOf(DependencyObject current) => current switch
    {
        Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current),
        FrameworkContentElement content => content.Parent,
        _ => LogicalTreeHelper.GetParent(current),
    };

    private void FocusCellName(Cell cell)
    {
        if (ContainerFor(cell) is { } container
            && FindDescendant<TextBox>(container, box => Equals(box.Tag, "CellName")) is { } nameBox)
        {
            nameBox.Focus();
            nameBox.SelectAll();
        }
    }

    /// <summary>Puts the cursor in an element's editor, or scrolls it into view if it has none.</summary>
    private void FocusElement(Element element)
    {
        if (DocumentEditorCell(element) is not { } cell || ContainerFor(cell) is not { } container)
            return;

        var presenter = FindDescendant<ContentPresenter>(container, p => ReferenceEquals(p.Content, element));
        if (presenter is null)
            return;

        if (FindDescendant<SourcePreviewPanel>(presenter, _ => true) is { } panel)
            panel.BeginEditing();
        else if (FindDescendant<TextBox>(presenter, box => box.IsVisible && box.Focusable) is { } box)
            box.Focus();
        else
            presenter.BringIntoView();
    }

    private Cell? DocumentEditorCell(Element element) => DocumentEditor.FindCell(ViewModel.Document, element);

    private ListBoxItem? ContainerFor(Cell cell)
    {
        Cells.ScrollIntoView(cell);
        Cells.UpdateLayout();
        return Cells.ItemContainerGenerator.ContainerFromItem(cell) as ListBoxItem;
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && predicate(match))
                return match;
            if (FindDescendant(child, predicate) is { } found)
                return found;
        }
        return null;
    }

    // --- Paste ----------------------------------------------------------------

    /// <summary>Enables Paste for images and files even when a text box (which only takes text) has focus.</summary>
    private void OnPreviewCanPaste(object sender, CanExecuteRoutedEventArgs e)
    {
        if (e.Command == ApplicationCommands.Paste && ClipboardData() is { } data && HasFilesOrImageOnly(data))
        {
            e.CanExecute = true;
            e.Handled = true;
        }
    }

    /// <summary>
    /// Files and images become elements wherever focus is. A lone URL pasted into an empty text
    /// element turns it into a link. Any other text is left for the focused text box to paste normally.
    /// </summary>
    private void OnPreviewPaste(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Paste || ClipboardData() is not { } data)
            return;

        if (HasFilesOrImageOnly(data))
        {
            InsertFromData(data, ViewModel.FileInsertMode, cell: null, after: null);
            e.Handled = true;
        }
        else if (Keyboard.FocusedElement is TextBox { DataContext: TextElement { Text.Length: 0 } empty }
                 && ReadText(data) is { } text
                 && ContentRecognizer.IsStandaloneUrl(text)
                 && DocumentEditorCell(empty) is { } cell)
        {
            var link = new LinkElement { Id = empty.Id, Url = text.Trim() };
            cell.Elements[cell.Elements.IndexOf(empty)] = link;
            ViewModel.SelectedElement = link;
            e.Handled = true;
        }
    }

    /// <summary>Paste when focus is not in a text box, for example after clicking an image: adds a new element.</summary>
    private void OnCanPaste(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = ClipboardData() is { } data && (HasFilesOrImageOnly(data) || ReadText(data) is not null);

    private void OnPaste(object sender, ExecutedRoutedEventArgs e)
    {
        if (ClipboardData() is { } data)
            InsertFromData(data, ViewModel.FileInsertMode, cell: null, after: null);
    }

    private static IDataObject? ClipboardData()
    {
        try
        {
            return Clipboard.GetDataObject();
        }
        catch (COMException)
        {
            // Another application has the clipboard open.
            return null;
        }
    }

    // --- Drag and drop --------------------------------------------------------

    /// <summary>Files and images are handled before text boxes, which would otherwise reject them.</summary>
    private void Cells_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!HasFilesOrImageOnly(e.Data))
            return;

        var attach = DropMode(e) == FileInsertMode.Attach;
        e.Effects = (attach ? DragDropEffects.Copy : DragDropEffects.Link) & e.AllowedEffects;
        if (e.Effects == DragDropEffects.None)
            e.Effects = e.AllowedEffects & (DragDropEffects.Copy | DragDropEffects.Link);
        e.Handled = true;
    }

    private void Cells_PreviewDrop(object sender, DragEventArgs e)
    {
        if (!HasFilesOrImageOnly(e.Data))
            return;

        DropOn(e, DropMode(e));
        e.Handled = true;
    }

    /// <summary>Text dragged onto the page outside any text box becomes a new element.</summary>
    private void Cells_DragOver(object sender, DragEventArgs e)
    {
        if (e.Handled)
            return;
        e.Effects = ReadText(e.Data) is not null ? DragDropEffects.Copy & e.AllowedEffects : DragDropEffects.None;
        e.Handled = true;
    }

    private void Cells_Drop(object sender, DragEventArgs e)
    {
        if (e.Handled || ReadText(e.Data) is null)
            return;

        DropOn(e, ViewModel.FileInsertMode);
        e.Handled = true;
    }

    private void DropOn(DragEventArgs e, FileInsertMode mode)
    {
        var (cell, element) = e.OriginalSource is DependencyObject target ? FindTarget(target) : (null, null);
        // Dropping below the last cell appends to it.
        cell ??= ViewModel.Document.Cells.LastOrDefault();
        InsertFromData(e.Data, mode, cell, element ?? cell?.Elements.LastOrDefault());
        Activate();
    }

    /// <summary>Holding Ctrl while dropping flips between referencing and attaching files.</summary>
    private FileInsertMode DropMode(DragEventArgs e)
    {
        var mode = ViewModel.FileInsertMode;
        if ((e.KeyStates & DragDropKeyStates.ControlKey) != 0)
            mode = mode == FileInsertMode.Attach ? FileInsertMode.Reference : FileInsertMode.Attach;
        return mode;
    }

    // --- Turning clipboard and drag data into elements --------------------------

    private void InsertFromData(IDataObject data, FileInsertMode mode, Cell? cell, Element? after)
    {
        IReadOnlyList<Element> elements;
        if (ReadFiles(data) is { Length: > 0 } files)
            elements = ViewModel.ElementsFromFiles(files, mode);
        else if (ReadText(data) is { } text)
            elements = [ContentRecognizer.FromText(text)];
        else if (ReadImage(data) is { } png)
            elements = [ContentRecognizer.FromImage(ViewModel.Document, png, ".png")];
        else
            return;

        ViewModel.InsertContent(elements, cell, after);
    }

    private static bool HasFilesOrImageOnly(IDataObject data) =>
        ReadFiles(data) is { Length: > 0 } || (ReadText(data) is null && HasImage(data));

    private static string[]? ReadFiles(IDataObject data) =>
        Safe(() => data.GetDataPresent(DataFormats.FileDrop) ? data.GetData(DataFormats.FileDrop) as string[] : null);

    private static string? ReadText(IDataObject data) =>
        Safe(() => data.GetDataPresent(DataFormats.UnicodeText) ? data.GetData(DataFormats.UnicodeText) as string : null) is { Length: > 0 } text
            ? text
            : null;

    private static bool HasImage(IDataObject data) =>
        Safe(() => data.GetDataPresent("PNG") || data.GetDataPresent(DataFormats.Bitmap));

    /// <summary>Reads an image as PNG bytes. Prefers the "PNG" format, which keeps transparency.</summary>
    private static byte[]? ReadImage(IDataObject data) => Safe(() =>
    {
        if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream stream)
            return stream.ToArray();

        if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        return null;
    });

    /// <summary>Data objects from other applications can fail in many ways; treat any failure as "not present".</summary>
    private static T? Safe<T>(Func<T?> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is COMException or ExternalException or OutOfMemoryException or NotSupportedException or InvalidOperationException)
        {
            return default;
        }
    }

    // --- Element actions ------------------------------------------------------

    private static Element? ElementOf(object sender) => sender switch
    {
        FrameworkElement fe => fe.DataContext as Element,
        FrameworkContentElement fce => fce.DataContext as Element,
        _ => null,
    };

    private static void Execute(ICommand command, object? parameter)
    {
        if (command.CanExecute(parameter))
            command.Execute(parameter);
    }

    private void MoveElementUp_Click(object sender, RoutedEventArgs e) => Execute(ViewModel.MoveElementUpCommand, ElementOf(sender));

    private void MoveElementDown_Click(object sender, RoutedEventArgs e) => Execute(ViewModel.MoveElementDownCommand, ElementOf(sender));

    private void DeleteElement_Click(object sender, RoutedEventArgs e) => Execute(ViewModel.DeleteElementCommand, ElementOf(sender));

    private void ConvertToText_Click(object sender, RoutedEventArgs e) => Execute(ViewModel.ConvertToTextCommand, ElementOf(sender));

    private void ConvertToMarkdown_Click(object sender, RoutedEventArgs e) => Execute(ViewModel.ConvertToMarkdownCommand, ElementOf(sender));

    private void ConvertToCode_Click(object sender, RoutedEventArgs e) => Execute(ViewModel.ConvertToCodeCommand, ElementOf(sender));

    private void ReplaceImage_Click(object sender, RoutedEventArgs e) => Execute(ViewModel.ReplaceImageCommand, ElementOf(sender));

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        if (ElementOf(sender) is LinkElement link)
            LinkLauncher.Open(link.Url);
    }

    private void RevealFile_Click(object sender, RoutedEventArgs e)
    {
        if (ElementOf(sender) is not FileReferenceElement reference)
            return;

        var path = reference.Path.Trim();
        if (File.Exists(path))
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        else if (Directory.Exists(path))
            Process.Start("explorer.exe", $"\"{path}\"");
        else
            ShowWarning($"The file was not found:\n{path}");
    }

    private bool TryGetAsset(object sender, out AssetElement element, out byte[] data)
    {
        data = [];
        element = (ElementOf(sender) as AssetElement)!;
        if (element is null)
            return false;
        if (ViewModel.Document.Assets.TryGet(element.Asset, out data))
            return true;

        ShowWarning("This file is missing from the document.");
        return false;
    }

    private static string FileNameFor(AssetElement element) =>
        element is AttachmentElement { FileName.Length: > 0 } attachment ? attachment.FileName : Path.GetFileName(element.Asset);

    private void SaveAsset_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetAsset(sender, out var element, out var data))
            return;

        var dialog = new SaveFileDialog { FileName = FileNameFor(element), Filter = "All files|*.*" };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            File.WriteAllBytes(dialog.FileName, data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Quire could not save the file.\n\n{ex.Message}");
        }
    }

    private void CopyImage_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetAsset(sender, out var element, out var data))
            return;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(data, writable: false);
            bitmap.EndInit();
            bitmap.Freeze();

            var clipboard = new DataObject();
            clipboard.SetImage(bitmap);
            if (Path.GetExtension(element.Asset).Equals(".png", StringComparison.OrdinalIgnoreCase))
                clipboard.SetData("PNG", new MemoryStream(data, writable: false));
            Clipboard.SetDataObject(clipboard, copy: true);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or COMException)
        {
            ShowError($"Quire could not copy the image.\n\n{ex.Message}");
        }
    }

    /// <summary>Writes the image to a temporary file and opens it with the default image viewer.</summary>
    private void OpenImage_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetAsset(sender, out var element, out var data))
            return;

        // Only open real image types, so a renamed asset cannot be launched as a program.
        var name = FileNameFor(element);
        if (!ContentRecognizer.IsImageFile(name))
            name = Path.ChangeExtension(name, ".png");

        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Quire", Guid.NewGuid().ToString("N")));
            var path = Path.Combine(folder.FullName, name);
            File.WriteAllBytes(path, data);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ShowError($"Quire could not open the image.\n\n{ex.Message}");
        }
    }
}
