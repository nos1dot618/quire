using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Quire.Storage;
using Quire.ViewModels;

namespace Quire.UI;

/// <summary>Window shell: dialogs, menus, zoom and full screen. Editing interactions are in MainWindow.Editing.cs.</summary>
public partial class MainWindow : Window, IDialogService
{
    private const string DocumentFilter = "Quire documents (*.quire)|*.quire|All files (*.*)|*.*";
    private const double ZoomStep = 0.1;
    private const double MinZoom = 0.5;
    private const double MaxZoom = 3.0;

    /// <summary>How often unsaved work is written to the recovery folder.</summary>
    private static readonly TimeSpan DraftInterval = TimeSpan.FromSeconds(20);

    private readonly DispatcherTimer _draftTimer;
    private WindowState _stateBeforeFullScreen;

    public MainWindow(RecoveryStore? recovery = null, SettingsStore? settings = null)
    {
        InitializeComponent();
        ViewModel = new MainViewModel(this,
            action => Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, action),
            recovery, settings);
        ThemeManager.Apply(ViewModel.Theme);
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Theme))
                ThemeManager.Apply(ViewModel.Theme);
        };
        ViewModel.Restored += (_, _) => KeepCaretAfterUndo();
        ViewModel.RenameRequested += (_, cell) => FocusCellName(cell);
        ViewModel.FocusElementRequested += (_, element) => FocusElement(element);
        DataContext = ViewModel;

        // Paste of images and files is handled before text boxes see it; see MainWindow.Editing.cs.
        CommandManager.AddPreviewCanExecuteHandler(this, OnPreviewCanPaste);
        CommandManager.AddPreviewExecutedHandler(this, OnPreviewPaste);
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, OnPaste, OnCanPaste));

        // Undo is document-wide, so it is taken over from text boxes (their own undo is disabled in the theme).
        CommandManager.AddPreviewCanExecuteHandler(this, OnPreviewCanUndo);
        CommandManager.AddPreviewExecutedHandler(this, OnPreviewUndo);

        _draftTimer = new DispatcherTimer(DraftInterval, DispatcherPriority.Background, (_, _) => ViewModel.SaveDraftIfNeeded(), Dispatcher);
        _draftTimer.Start();
    }

    private void OnPreviewCanUndo(object sender, CanExecuteRoutedEventArgs e)
    {
        var command = e.Command == ApplicationCommands.Undo ? ViewModel.UndoCommand
            : e.Command == ApplicationCommands.Redo ? ViewModel.RedoCommand
            : null;
        if (command is null)
            return;
        e.CanExecute = command.CanExecute(null);
        e.Handled = true;
    }

    private void OnPreviewUndo(object sender, ExecutedRoutedEventArgs e)
    {
        var command = e.Command == ApplicationCommands.Undo ? ViewModel.UndoCommand
            : e.Command == ApplicationCommands.Redo ? ViewModel.RedoCommand
            : null;
        if (command is null)
            return;
        if (command.CanExecute(null))
            command.Execute(null);
        e.Handled = true;
    }

    /// <summary>When undo rewrites the text being edited, put the caret at the end instead of the start.</summary>
    private void KeepCaretAfterUndo()
    {
        if (Keyboard.FocusedElement is TextBox box)
            box.CaretIndex = box.Text.Length;
    }

    public MainViewModel ViewModel { get; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!ViewModel.ConfirmClose())
            e.Cancel = true;
        else
            _draftTimer.Stop();
        base.OnClosing(e);
    }

    /// <summary>Ctrl + mouse wheel zooms the document, as in browsers and editors.</summary>
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            SetZoom(Zoom.ScaleX + Math.Sign(e.Delta) * ZoomStep);
            e.Handled = true;
            return;
        }
        base.OnPreviewMouseWheel(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        switch (e.Key)
        {
            case Key.OemPlus or Key.Add when ctrl:
                SetZoom(Zoom.ScaleX + ZoomStep);
                break;
            case Key.OemMinus or Key.Subtract when ctrl:
                SetZoom(Zoom.ScaleX - ZoomStep);
                break;
            case Key.D0 or Key.NumPad0 when ctrl:
                SetZoom(1);
                break;
            case Key.F11:
                ToggleFullScreen();
                break;
            case Key.F when ctrl:
                ShowFind();
                break;
            case Key.F3:
                Step(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : +1);
                break;
            case Key.F1:
                ShowShortcuts();
                break;
            case Key.Escape when FindBar.Visibility == Visibility.Visible:
                HideFind();
                break;
            default:
                base.OnPreviewKeyDown(e);
                return;
        }
        e.Handled = true;
    }

    // --- IDialogService --------------------------------------------------------

    public string? ShowOpenDocument()
    {
        var dialog = new OpenFileDialog { Filter = DocumentFilter, DefaultExt = DocumentStore.FileExtension };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    public string? ShowSaveDocument(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = DocumentFilter,
            DefaultExt = DocumentStore.FileExtension,
            AddExtension = true,
            FileName = suggestedName,
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    public string? ShowOpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    public string? ShowSaveFile(string title, string filter, string suggestedName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = suggestedName, AddExtension = true };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    public SaveChangesChoice AskSaveChanges(string documentName) =>
        MessageBox.Show(this, $"Do you want to save changes to {documentName}?", "Quire",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Warning) switch
        {
            MessageBoxResult.Yes => SaveChangesChoice.Save,
            MessageBoxResult.No => SaveChangesChoice.Discard,
            _ => SaveChangesChoice.Cancel,
        };

    public RecoveryChoice AskRecover(string documentName, DateTime savedAtUtc, int otherDrafts)
    {
        var others = otherDrafts > 0 ? $"\n\n{otherDrafts} older unsaved draft(s) will be offered next time." : "";
        var message = $"Quire closed before \"{documentName}\" was saved. Unsaved changes from {savedAtUtc.ToLocalTime():g} can be recovered.\n\n"
            + $"Yes: restore them now.\nNo: delete them.\nCancel: decide next time.{others}";
        return MessageBox.Show(this, message, "Recover Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        {
            MessageBoxResult.Yes => RecoveryChoice.Restore,
            MessageBoxResult.No => RecoveryChoice.Delete,
            _ => RecoveryChoice.Later,
        };
    }

    public void ShowError(string message) =>
        MessageBox.Show(this, message, "Quire", MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowWarning(string message) =>
        MessageBox.Show(this, message, "Quire", MessageBoxButton.OK, MessageBoxImage.Warning);

    // --- Menu handlers --------------------------------------------------------

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(Zoom.ScaleX + ZoomStep);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(Zoom.ScaleX - ZoomStep);

    private void ZoomReset_Click(object sender, RoutedEventArgs e) => SetZoom(1);

    private void SetZoom(double value)
    {
        value = Math.Round(Math.Clamp(value, MinZoom, MaxZoom), 2);
        Zoom.ScaleX = value;
        Zoom.ScaleY = value;
        // "Display" text is hinted to whole pixels at 100% and turns blurry when scaled.
        // "Ideal" text is laid out for any size, so it stays sharp while zoomed.
        TextOptions.SetTextFormattingMode(Cells, value == 1 ? TextFormattingMode.Display : TextFormattingMode.Ideal);
    }

    private void FullScreen_Click(object sender, RoutedEventArgs e) => ToggleFullScreen();

    private void ToggleFullScreen()
    {
        if (WindowStyle == WindowStyle.None)
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = _stateBeforeFullScreen;
        }
        else
        {
            _stateBeforeFullScreen = WindowState;
            WindowStyle = WindowStyle.None;
            // Re-maximize so the borderless window covers the taskbar.
            WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var version = typeof(MainWindow).Assembly.GetName().Version;
        MessageBox.Show(this, $"Quire {version?.ToString(3)}\nA local-first scratchpad for mixed content.", "About Quire",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
