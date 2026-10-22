using System.IO;
using System.Windows;
using Quire.Storage;
using Quire.UI;
using Quire.ViewModels;

namespace Quire;

public partial class App : Application
{
    private RecoveryStore? _recovery;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _recovery = new RecoveryStore(RecoveryStore.DefaultRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Quire still works without crash recovery; it just cannot keep drafts.
            _recovery = null;
        }

        var window = new MainWindow(_recovery, new SettingsStore(SettingsStore.DefaultPath));
        window.Show();

        if (OfferRecovery(window))
            return;

        // Opening a .quire file from Explorer passes its path as the first argument.
        if (e.Args.Length > 0)
            window.ViewModel.OpenPath(e.Args[0]);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // A clean exit removes this session's draft; a crash leaves it for next time.
        _recovery?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Offers the newest draft left by a session that did not exit cleanly. Returns true if it was restored.</summary>
    private bool OfferRecovery(MainWindow window)
    {
        if (_recovery?.FindOrphanedDrafts() is not [var newest, ..] drafts)
            return false;

        switch (window.AskRecover(newest.DisplayName, newest.SavedAtUtc, drafts.Count - 1))
        {
            case RecoveryChoice.Restore:
                window.ViewModel.RestoreDraft(newest);
                return true;
            case RecoveryChoice.Delete:
                _recovery.Discard(newest);
                return false;
            default:
                return false;
        }
    }
}
