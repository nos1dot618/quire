namespace Quire.ViewModels;

public enum RecoveryChoice
{
    Restore,
    Delete,
    Later,
}

public enum SaveChangesChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>The native dialogs the view model needs, implemented by the main window.</summary>
public interface IDialogService
{
    string? ShowOpenDocument();

    string? ShowSaveDocument(string suggestedName);

    /// <summary>Asks for a file to insert. <paramref name="filter"/> uses the Win32 filter syntax.</summary>
    string? ShowOpenFile(string title, string filter);

    /// <summary>Asks where to write a file, for example an export. <paramref name="filter"/> uses the Win32 filter syntax.</summary>
    string? ShowSaveFile(string title, string filter, string suggestedName);

    SaveChangesChoice AskSaveChanges(string documentName);

    /// <summary>Offers to restore unsaved work from a session that ended unexpectedly.</summary>
    RecoveryChoice AskRecover(string documentName, DateTime savedAtUtc, int otherDrafts);

    void ShowError(string message);

    void ShowWarning(string message);
}
