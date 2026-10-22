using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Quire.Editing;
using Quire.Models;

namespace Quire.UI;

/// <summary>Find bar, recent documents menu, and help.</summary>
public partial class MainWindow
{
    private IReadOnlyList<SearchMatch> _matches = [];
    private int _matchIndex = -1;
    private SearchMatch? _shownMatch;

    // --- Find -----------------------------------------------------------------

    private void ShowFind()
    {
        FindBar.Visibility = Visibility.Visible;
        // Seed with the selected text, as most editors do.
        if (Keyboard.FocusedElement is TextBox { SelectedText: { Length: > 0 and < 100 } selected } box && box != FindBox && !selected.Contains('\n'))
            FindBox.Text = selected;
        FindBox.Focus();
        FindBox.SelectAll();
        Search(keepPosition: true);
    }

    private void HideFind()
    {
        FindBar.Visibility = Visibility.Collapsed;
        // Continue editing at the match that was shown.
        if (_shownMatch is { } match && TextBoxFor(match) is { } box)
        {
            box.Focus();
            box.Select(match.Index, match.Length);
        }
        _shownMatch = null;
    }

    private void Find_Click(object sender, RoutedEventArgs e) => ShowFind();

    private void CloseFind_Click(object sender, RoutedEventArgs e) => HideFind();

    private void FindNext_Click(object sender, RoutedEventArgs e) => Step(+1);

    private void FindPrevious_Click(object sender, RoutedEventArgs e) => Step(-1);

    private void FindBox_TextChanged(object sender, TextChangedEventArgs e) => Search(keepPosition: false);

    private void FindBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Step(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : +1);
                e.Handled = true;
                break;
            case Key.Escape:
                HideFind();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Finds all matches and shows the first one at or after the current position.</summary>
    private void Search(bool keepPosition)
    {
        var previous = _shownMatch;
        _matches = DocumentSearch.Find(ViewModel.Document, FindBox.Text);
        _matchIndex = -1;
        if (_matches.Count == 0)
        {
            _shownMatch = null;
            UpdateFindStatus();
            return;
        }

        _matchIndex = keepPosition && previous is not null ? Math.Max(0, IndexAtOrAfter(previous)) : Math.Max(0, IndexAtOrAfterSelection());
        Show(_matches[_matchIndex]);
    }

    private void Step(int direction)
    {
        if (FindBar.Visibility != Visibility.Visible)
        {
            ShowFind();
            return;
        }

        // The document may have changed since the last search; search again from the shown match.
        var previous = _shownMatch;
        _matches = DocumentSearch.Find(ViewModel.Document, FindBox.Text);
        if (_matches.Count == 0)
        {
            _matchIndex = -1;
            _shownMatch = null;
            UpdateFindStatus();
            return;
        }

        var current = previous is null ? -1 : IndexOf(previous);
        if (current < 0)
            current = previous is null ? -1 : IndexAtOrAfter(previous) - (direction > 0 ? 1 : 0);
        _matchIndex = ((current + direction) % _matches.Count + _matches.Count) % _matches.Count;
        Show(_matches[_matchIndex]);
    }

    private int IndexOf(SearchMatch match)
    {
        for (var i = 0; i < _matches.Count; i++)
        {
            var m = _matches[i];
            if (m.Cell == match.Cell && m.Element == match.Element && m.Field == match.Field && m.Index == match.Index)
                return i;
        }
        return -1;
    }

    /// <summary>The first match at or after a previous match's position (by cell, element, field, then offset).</summary>
    private int IndexAtOrAfter(SearchMatch reference)
    {
        var key = PositionKey(reference);
        for (var i = 0; i < _matches.Count; i++)
        {
            if (Compare(PositionKey(_matches[i]), key) >= 0)
                return i;
        }
        return 0;
    }

    /// <summary>Starts from the selected cell, so searching begins where the user is working.</summary>
    private int IndexAtOrAfterSelection()
    {
        if (ViewModel.SelectedCell is not { } cell)
            return 0;
        var cellIndex = ViewModel.Document.Cells.IndexOf(cell);
        for (var i = 0; i < _matches.Count; i++)
        {
            if (ViewModel.Document.Cells.IndexOf(_matches[i].Cell) >= cellIndex)
                return i;
        }
        return 0;
    }

    private (int Cell, int Element, int Field, int Offset) PositionKey(SearchMatch match)
    {
        var cell = ViewModel.Document.Cells.IndexOf(match.Cell);
        var element = match.Element is null ? -1 : match.Cell.Elements.IndexOf(match.Element);
        var field = match.Element is null ? 0 : DocumentSearch.SearchableFields(match.Element).Select(f => f.Field).ToList().IndexOf(match.Field);
        return (cell, element, field, match.Index);
    }

    private static int Compare((int, int, int, int) a, (int, int, int, int) b) => a.CompareTo(b);

    /// <summary>Scrolls to a match and selects it in its text box, keeping keyboard focus in the find box.</summary>
    private void Show(SearchMatch match)
    {
        _shownMatch = match;
        ViewModel.SelectedCell = match.Cell;
        if (match.Element is not null)
            ViewModel.SelectedElement = match.Element;
        UpdateFindStatus();

        if (ContainerFor(match.Cell) is not { } container)
            return;

        // Markdown and code show a preview; reveal the source so the match can be selected.
        if (match.Element is not null
            && FindDescendant<ContentPresenter>(container, p => ReferenceEquals(p.Content, match.Element)) is { } presenter
            && FindDescendant<SourcePreviewPanel>(presenter, _ => true) is { } panel)
        {
            panel.ShowSource();
            presenter.UpdateLayout();
        }

        if (TextBoxFor(match) is not { } box)
            return;

        var length = Math.Min(match.Length, Math.Max(0, box.Text.Length - match.Index));
        box.Select(Math.Min(match.Index, box.Text.Length), length);
        box.UpdateLayout();
        var rect = box.GetRectFromCharacterIndex(Math.Min(match.Index, box.Text.Length));
        if (rect.IsEmpty)
            box.BringIntoView();
        else
            box.BringIntoView(new Rect(rect.X, rect.Y - 40, Math.Max(rect.Width, 1), rect.Height + 80));
    }

    /// <summary>The text box that edits a match's field: a cell's name box, or the element box bound to that property.</summary>
    private TextBox? TextBoxFor(SearchMatch match)
    {
        if (ContainerFor(match.Cell) is not { } container)
            return null;

        if (match.Element is null)
            return FindDescendant<TextBox>(container, box => Equals(box.Tag, "CellName"));

        var presenter = FindDescendant<ContentPresenter>(container, p => ReferenceEquals(p.Content, match.Element));
        return presenter is null
            ? null
            : FindDescendant<TextBox>(presenter, box =>
                BindingOperations.GetBinding(box, TextBox.TextProperty)?.Path?.Path == match.Field);
    }

    private void UpdateFindStatus() =>
        FindStatus.Text = FindBox.Text.Length == 0 ? ""
            : _matches.Count == 0 ? "No results"
            : $"{_matchIndex + 1} of {_matches.Count}";

    // --- Recent documents -----------------------------------------------------

    private void RecentMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, RecentMenu))
            return;

        RecentMenu.Items.Clear();
        var recent = ViewModel.RecentDocuments.ToList();
        if (recent.Count == 0)
        {
            RecentMenu.Items.Add(new MenuItem { Header = "(none)", IsEnabled = false });
            return;
        }

        for (var i = 0; i < recent.Count; i++)
        {
            var path = recent[i];
            // Access keys 1-9 then 0, as in classic Windows recent-file lists.
            var key = i < 9 ? $"_{i + 1}" : i == 9 ? "1_0" : $"{i + 1}";
            RecentMenu.Items.Add(new MenuItem
            {
                Header = $"{key}  {Path.GetFileName(path).Replace("_", "__")}",
                ToolTip = path,
                Command = ViewModel.OpenRecentCommand,
                CommandParameter = path,
            });
        }
        RecentMenu.Items.Add(new Separator());
        RecentMenu.Items.Add(new MenuItem { Header = "_Clear Recent Documents", Command = ViewModel.ClearRecentCommand });
    }

    // --- Help -----------------------------------------------------------------

    private void Shortcuts_Click(object sender, RoutedEventArgs e) => ShowShortcuts();

    private void ShowShortcuts() =>
        MessageBox.Show(this, """
            Documents
              Ctrl+N  New            Ctrl+O  Open
              Ctrl+S  Save           Ctrl+Shift+S  Save As

            Editing
              Ctrl+Z  Undo           Ctrl+Y  Redo
              Ctrl+V  Paste (images and files become elements)
              Ctrl+F  Find           F3 / Shift+F3  Next / previous match

            Cells
              Ctrl+Shift+N  New cell        F2  Rename cell
              Ctrl+D  Duplicate cell        Alt+Up / Alt+Down  Move cell

            Elements
              Alt+Shift+Up / Alt+Shift+Down  Move element
              Ctrl+Shift+Delete  Delete element
              Hold Ctrl while dropping a file to switch reference / attach

            View
              Ctrl+= / Ctrl+- or Ctrl+wheel  Zoom      Ctrl+0  Reset zoom
              F11  Full screen
            """, "Keyboard Shortcuts", MessageBoxButton.OK, MessageBoxImage.None);
}
