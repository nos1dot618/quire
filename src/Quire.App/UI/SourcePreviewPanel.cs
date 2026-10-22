using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using Quire.Models;

namespace Quire.UI;

/// <summary>
/// Hosts a source editor and a rendered preview of the same element. The preview is shown while
/// preview mode is on and the editor is not focused; clicking or tabbing into the preview switches
/// to the editor, and leaving the editor switches back. The source text is the only stored state.
/// </summary>
public sealed class SourcePreviewPanel : Grid
{
    public static readonly DependencyProperty IsPreviewEnabledProperty = DependencyProperty.Register(
        nameof(IsPreviewEnabled), typeof(bool), typeof(SourcePreviewPanel),
        new PropertyMetadata(true, (d, _) => ((SourcePreviewPanel)d).UpdateMode()));

    public static readonly DependencyProperty EditorProperty = DependencyProperty.Register(
        nameof(Editor), typeof(TextBox), typeof(SourcePreviewPanel),
        new PropertyMetadata(null, (d, e) => ((SourcePreviewPanel)d).AttachEditor((TextBox?)e.OldValue, (TextBox?)e.NewValue)));

    public static readonly DependencyProperty PreviewProperty = DependencyProperty.Register(
        nameof(Preview), typeof(FrameworkElement), typeof(SourcePreviewPanel),
        new PropertyMetadata(null, (d, e) => ((SourcePreviewPanel)d).AttachPreview((FrameworkElement?)e.OldValue, (FrameworkElement?)e.NewValue)));

    private bool _showingPreview;

    public SourcePreviewPanel()
    {
        Loaded += (_, _) => UpdateMode();
        DataContextChanged += OnDataContextChanged;
    }

    public bool IsPreviewEnabled
    {
        get => (bool)GetValue(IsPreviewEnabledProperty);
        set => SetValue(IsPreviewEnabledProperty, value);
    }

    public TextBox? Editor
    {
        get => (TextBox?)GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    public FrameworkElement? Preview
    {
        get => (FrameworkElement?)GetValue(PreviewProperty);
        set => SetValue(PreviewProperty, value);
    }

    /// <summary>Shows the editor and gives it keyboard focus.</summary>
    public void BeginEditing()
    {
        if (Editor is not { } editor)
            return;

        ShowPreview(false);
        // Focus once layout has made the editor visible.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            editor.Focus();
            Keyboard.Focus(editor);
        });
    }

    /// <summary>Shows the source without taking focus, for example to reveal a find result. Leaving or re-entering the editor restores normal behaviour.</summary>
    public void ShowSource() => ShowPreview(false);

    private void AttachEditor(TextBox? oldEditor, TextBox? newEditor)
    {
        if (oldEditor is not null)
        {
            oldEditor.LostKeyboardFocus -= OnEditorFocusChanged;
            oldEditor.GotKeyboardFocus -= OnEditorFocusChanged;
        }
        if (newEditor is not null)
        {
            newEditor.LostKeyboardFocus += OnEditorFocusChanged;
            newEditor.GotKeyboardFocus += OnEditorFocusChanged;
        }
        UpdateMode();
    }

    private void AttachPreview(FrameworkElement? oldPreview, FrameworkElement? newPreview)
    {
        if (oldPreview is not null)
        {
            oldPreview.PreviewMouseLeftButtonUp -= OnPreviewClicked;
            oldPreview.GotKeyboardFocus -= OnPreviewFocused;
        }
        if (newPreview is not null)
        {
            newPreview.Focusable = true;
            newPreview.PreviewMouseLeftButtonUp += OnPreviewClicked;
            newPreview.GotKeyboardFocus += OnPreviewFocused;
        }
        UpdateMode();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldElement)
            oldElement.PropertyChanged -= OnElementChanged;
        if (e.NewValue is INotifyPropertyChanged newElement)
            newElement.PropertyChanged += OnElementChanged;
        UpdateMode();
    }

    private void OnElementChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Text changes while editing are rendered when the preview is shown again.
        if (_showingPreview)
            Render();
        else if (Editor is { IsKeyboardFocusWithin: false })
            UpdateMode();
    }

    private void OnEditorFocusChanged(object sender, KeyboardFocusChangedEventArgs e) => UpdateMode();

    private void OnPreviewClicked(object sender, MouseButtonEventArgs e)
    {
        // Let hyperlinks in the preview work, and keep a text selection the user just made.
        if (e.OriginalSource is DependencyObject source && IsInsideHyperlink(source))
            return;
        if (Preview is RichTextBox { Selection.IsEmpty: false })
            return;

        BeginEditing();
    }

    private void OnPreviewFocused(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Reaching the preview with Tab goes straight to editing.
        if (Keyboard.IsKeyDown(Key.Tab))
            BeginEditing();
    }

    private void UpdateMode()
    {
        if (Editor is not { } editor || Preview is null)
            return;

        var hasContent = DataContext is TextualElement { Text.Length: > 0 };
        ShowPreview(IsPreviewEnabled && hasContent && !editor.IsKeyboardFocusWithin);
    }

    private void ShowPreview(bool show)
    {
        if (Editor is not { } editor || Preview is not { } preview)
            return;

        if (show && !_showingPreview)
            Render();

        _showingPreview = show;
        editor.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        preview.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Renders the element into the preview: Markdown as formatted text, code with highlighting.</summary>
    private void Render()
    {
        if (Preview is not RichTextBox preview)
            return;

        switch (DataContext)
        {
            case MarkdownElement markdown:
                preview.Document = MarkdownRenderer.Render(markdown.Text);
                break;
            case CodeElement code:
                var paragraph = new Paragraph { Margin = new Thickness(0) };
                MarkdownRenderer.AddHighlighted(paragraph.Inlines, code.Text, code.Language);
                preview.Document = new FlowDocument(paragraph)
                {
                    PagePadding = new Thickness(0),
                    FontFamily = preview.FontFamily,
                    FontSize = preview.FontSize,
                };
                break;
        }
    }

    private static bool IsInsideHyperlink(DependencyObject source)
    {
        for (DependencyObject? current = source; current is not null;
             current = current is FrameworkContentElement content ? content.Parent : LogicalTreeHelper.GetParent(current))
        {
            if (current is Hyperlink)
                return true;
            if (current is SourcePreviewPanel)
                return false;
        }
        return false;
    }
}
