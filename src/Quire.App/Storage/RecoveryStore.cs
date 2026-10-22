using System.IO;
using System.Text.Json;
using Quire.Models;

namespace Quire.Storage;

/// <summary>A draft left behind by a Quire session that ended without saving (a crash, power loss, or killed process).</summary>
public sealed record RecoveryDraft(string Folder, string? OriginalPath, DateTime SavedAtUtc)
{
    public string DocumentPath => Path.Combine(Folder, RecoveryStore.DraftFileName);

    public string DisplayName => OriginalPath is null ? "Untitled" : Path.GetFileName(OriginalPath);
}

/// <summary>
/// Keeps a recoverable copy of unsaved work. Each running session owns a folder holding a lock
/// file (open for the session's lifetime), the latest draft, and a small info file. A folder whose
/// lock file can be opened belongs to a session that is no longer running, so its draft is an orphan
/// that can be offered for recovery.
/// </summary>
public sealed class RecoveryStore : IDisposable
{
    internal const string DraftFileName = "draft.quire";
    private const string InfoFileName = "draft.json";
    private const string LockFileName = "session.lock";

    private readonly string _root;
    private readonly string _sessionFolder;
    private readonly FileStream _lock;

    public RecoveryStore(string root)
    {
        _root = root;
        _sessionFolder = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sessionFolder);
        _lock = new FileStream(Path.Combine(_sessionFolder, LockFileName), FileMode.Create, FileAccess.ReadWrite, FileShare.None,
            bufferSize: 1, FileOptions.DeleteOnClose);
    }

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quire", "Recovery");

    /// <summary>Writes the current state as this session's draft, replacing the previous one.</summary>
    public void SaveDraft(QuireDocument document, string? originalPath)
    {
        DocumentStore.Save(document, Path.Combine(_sessionFolder, DraftFileName));
        var info = new DraftInfo(originalPath, DateTime.UtcNow);
        WriteAtomically(Path.Combine(_sessionFolder, InfoFileName), JsonSerializer.Serialize(info));
    }

    /// <summary>Removes this session's draft, for example after a successful save.</summary>
    public void ClearDraft()
    {
        TryDelete(Path.Combine(_sessionFolder, DraftFileName));
        TryDelete(Path.Combine(_sessionFolder, InfoFileName));
    }

    /// <summary>Drafts from sessions that are no longer running, newest first. Empty orphan folders are cleaned up.</summary>
    public IReadOnlyList<RecoveryDraft> FindOrphanedDrafts()
    {
        var drafts = new List<RecoveryDraft>();
        if (!Directory.Exists(_root))
            return drafts;

        foreach (var folder in Directory.EnumerateDirectories(_root))
        {
            if (string.Equals(folder, _sessionFolder, StringComparison.OrdinalIgnoreCase) || IsLocked(folder))
                continue;

            if (!File.Exists(Path.Combine(folder, DraftFileName)))
            {
                TryDeleteFolder(folder);
                continue;
            }

            var info = ReadInfo(folder);
            drafts.Add(new RecoveryDraft(folder, info?.OriginalPath, info?.SavedAtUtc ?? Directory.GetLastWriteTimeUtc(folder)));
        }

        return drafts.OrderByDescending(draft => draft.SavedAtUtc).ToList();
    }

    /// <summary>Deletes an orphaned draft once it has been restored or declined.</summary>
    public void Discard(RecoveryDraft draft)
    {
        if (!IsLocked(draft.Folder))
            TryDeleteFolder(draft.Folder);
    }

    /// <summary>Releases the session lock but leaves the folder, as a crash would. For tests.</summary>
    internal void SimulateCrash() => _lock.Dispose();

    public void Dispose()
    {
        // A clean exit leaves nothing to recover.
        _lock.Dispose();
        TryDeleteFolder(_sessionFolder);
    }

    private static bool IsLocked(string folder)
    {
        var lockPath = Path.Combine(folder, LockFileName);
        if (!File.Exists(lockPath))
            return false;
        try
        {
            using var stream = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static DraftInfo? ReadInfo(string folder)
    {
        try
        {
            return JsonSerializer.Deserialize<DraftInfo>(File.ReadAllText(Path.Combine(folder, InfoFileName)));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteAtomically(string path, string contents)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteFolder(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record DraftInfo(string? OriginalPath, DateTime SavedAtUtc);
}
