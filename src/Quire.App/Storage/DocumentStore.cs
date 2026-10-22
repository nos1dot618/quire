using System.IO;
using System.IO.Compression;
using System.Text;
using Quire.Models;

namespace Quire.Storage;

/// <summary>
/// Reads and writes .quire files: a ZIP archive holding document.json and an assets/ folder.
/// </summary>
public static class DocumentStore
{
    public const string FileExtension = ".quire";
    public const string ManifestEntry = "document.json";

    /// <summary>Upper bound on the manifest size, so a hostile archive cannot exhaust memory.</summary>
    private const long MaxManifestBytes = 256L * 1024 * 1024;

    public static QuireDocument Load(string path)
    {
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(path);
        }
        catch (InvalidDataException ex)
        {
            throw new QuireFormatException("The file is not a Quire document (it is not a valid archive).", ex);
        }

        using (archive)
        {
            var manifest = archive.GetEntry(ManifestEntry)
                ?? throw new QuireFormatException("The file is not a Quire document (document.json is missing).");
            if (manifest.Length > MaxManifestBytes)
                throw new QuireFormatException("The document manifest is too large.");

            string json;
            try
            {
                using var reader = new StreamReader(manifest.Open(), Encoding.UTF8);
                json = reader.ReadToEnd();
            }
            catch (InvalidDataException ex)
            {
                throw new QuireFormatException("The document manifest is corrupted.", ex);
            }

            var document = DocumentSerializer.FromJson(json);

            foreach (var assetPath in ReferencedAssets(document).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var entry = IsSafeAssetPath(assetPath) ? archive.GetEntry(assetPath) : null;
                if (entry is null)
                {
                    document.MissingAssets.Add(assetPath);
                    continue;
                }

                try
                {
                    using var stream = entry.Open();
                    using var buffer = new MemoryStream();
                    stream.CopyTo(buffer);
                    document.Assets.Set(assetPath, buffer.ToArray());
                }
                catch (InvalidDataException)
                {
                    document.MissingAssets.Add(assetPath);
                }
            }

            return document;
        }
    }

    /// <summary>
    /// Saves to a temporary file beside the target and then swaps it in, so an interrupted save
    /// never leaves a half-written document in place of the previous one.
    /// </summary>
    public static void Save(QuireDocument document, string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                WriteArchive(document, stream);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(fullPath))
                File.Replace(tempPath, fullPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(tempPath, fullPath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    internal static void WriteArchive(QuireDocument document, Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        var manifest = archive.CreateEntry(ManifestEntry, CompressionLevel.Optimal);
        using (var writer = new StreamWriter(manifest.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            writer.Write(DocumentSerializer.ToJson(document));

        // Only referenced assets are written, so deleted images do not linger in the file.
        foreach (var assetPath in ReferencedAssets(document).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!document.Assets.TryGet(assetPath, out var data))
                continue;

            // Images and GIFs are already compressed; storing them avoids wasted work.
            var level = IsCompressedFormat(assetPath) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
            using var entryStream = archive.CreateEntry(assetPath, level).Open();
            entryStream.Write(data);
        }
    }

    private static IEnumerable<string> ReferencedAssets(QuireDocument document) =>
        document.Cells
            .SelectMany(cell => cell.Elements)
            .OfType<AssetElement>()
            .Select(element => element.Asset)
            .Where(asset => asset.Length > 0);

    private static bool IsSafeAssetPath(string path) =>
        path.StartsWith(AssetStore.Folder, StringComparison.Ordinal)
        && !path.Contains("..", StringComparison.Ordinal)
        && !path.Contains('\\');

    private static bool IsCompressedFormat(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".gif" or ".jpg" or ".jpeg" or ".webp" or ".zip";
}
