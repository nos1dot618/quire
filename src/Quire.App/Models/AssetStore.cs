namespace Quire.Models;

/// <summary>
/// Binary assets keyed by their archive path. Assets are immutable once added, so elements and
/// duplicated cells can share them by path.
/// </summary>
public sealed class AssetStore
{
    public const string Folder = "assets/";

    private readonly Dictionary<string, byte[]> _assets = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> Paths => _assets.Keys;

    public bool Contains(string path) => _assets.ContainsKey(path);

    public bool TryGet(string path, out byte[] data) => _assets.TryGetValue(path, out data!);

    /// <summary>Stores data under an exact path. Used when loading an archive.</summary>
    public void Set(string path, byte[] data) => _assets[path] = data;

    /// <summary>Stores data under a new unique path such as <c>assets/image-003.png</c> and returns that path.</summary>
    public string Add(byte[] data, string prefix, string extension)
    {
        extension = NormalizeExtension(extension);
        // Numbers are unique per prefix regardless of extension, so image-001.png and image-001.jpg never coexist.
        for (var n = 1; ; n++)
        {
            var stem = $"{Folder}{prefix}-{n:000}";
            if (_assets.Keys.Any(existing => existing.Equals(stem, StringComparison.OrdinalIgnoreCase)
                    || existing.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase)))
                continue;

            var path = stem + extension;
            _assets[path] = data;
            return path;
        }
    }

    private static string NormalizeExtension(string extension)
    {
        extension = extension.Trim().ToLowerInvariant();
        if (extension.Length == 0)
            return "";
        if (!extension.StartsWith('.'))
            extension = "." + extension;
        // Keep archive entry names simple and safe.
        return extension.All(c => c == '.' || char.IsAsciiLetterOrDigit(c)) ? extension : ".bin";
    }
}
