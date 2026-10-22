using System.Text.Json;
using System.Text.Json.Nodes;
using Quire.Models;

namespace Quire.Storage;

/// <summary>Maps the document model to and from the document.json manifest. Binary assets are handled by <see cref="DocumentStore"/>.</summary>
public static class DocumentSerializer
{
    public const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string ToJson(QuireDocument document)
    {
        var cells = new JsonArray();
        foreach (var cell in document.Cells)
        {
            var elements = new JsonArray();
            foreach (var element in cell.Elements)
                elements.Add(WriteElement(element));

            cells.Add(new JsonObject
            {
                ["id"] = cell.Id,
                ["name"] = cell.Name,
                ["elements"] = elements,
            });
        }

        var root = new JsonObject
        {
            ["formatVersion"] = CurrentFormatVersion,
            ["title"] = document.Title,
            ["cells"] = cells,
        };
        return root.ToJsonString(WriteOptions);
    }

    /// <summary>
    /// Upgrades a manifest one version at a time. <c>Migrations[0]</c> turns version 1 into 2,
    /// <c>Migrations[1]</c> turns 2 into 3, and so on. Version 1 is current, so the list is empty.
    /// </summary>
    private static readonly IReadOnlyList<Func<JsonObject, JsonObject>> Migrations = [];

    internal static JsonObject Migrate(JsonObject root, int fromVersion, IReadOnlyList<Func<JsonObject, JsonObject>> migrations)
    {
        for (var version = fromVersion; version < migrations.Count + 1; version++)
        {
            root = migrations[version - 1](root);
            root["formatVersion"] = version + 1;
        }
        return root;
    }

    public static QuireDocument FromJson(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new QuireFormatException("The document manifest is not valid JSON.", ex);
        }

        if (node is not JsonObject root)
            throw new QuireFormatException("The document manifest must be a JSON object.");

        var version = ReadInt(root, "formatVersion")
            ?? throw new QuireFormatException("The document manifest has no formatVersion.");
        if (version < 1)
            throw new QuireFormatException($"Format version {version} is not valid.");
        if (version > CurrentFormatVersion)
            throw new QuireFormatException(
                $"This document was saved by a newer version of Quire (format {version}). This version supports format {CurrentFormatVersion}.");

        root = Migrate(root, version, Migrations);

        var document = new QuireDocument { Title = ReadString(root, "title") ?? "Untitled" };

        if (root["cells"] is not JsonArray cells)
            throw new QuireFormatException("The document manifest has no cells array.");

        // Ids must be unique for undo and editing to work. Missing or repeated ids are replaced.
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        string UniqueId(string? id) => id is { Length: > 0 } && seenIds.Add(id) ? id : NewUniqueId(seenIds);

        foreach (var cellNode in cells)
        {
            if (cellNode is not JsonObject cellObject)
                throw new QuireFormatException("A cell entry is not a JSON object.");

            var cell = new Cell
            {
                Id = UniqueId(ReadString(cellObject, "id")),
                Name = ReadString(cellObject, "name") ?? "",
            };

            if (cellObject["elements"] is JsonArray elements)
            {
                foreach (var elementNode in elements)
                {
                    if (elementNode is not JsonObject elementObject)
                        throw new QuireFormatException($"An element in cell \"{cell.Name}\" is not a JSON object.");
                    cell.Elements.Add(ReadElement(elementObject, UniqueId(ReadString(elementObject, "id"))));
                }
            }

            document.Cells.Add(cell);
        }

        return document;
    }

    private static JsonObject WriteElement(Element element)
    {
        if (element is UnknownElement unknown)
            return (JsonObject)unknown.Raw.DeepClone();

        var json = new JsonObject { ["id"] = element.Id, ["type"] = element.TypeName };
        switch (element)
        {
            case CodeElement code:
                json["language"] = code.Language;
                json["text"] = code.Text;
                break;
            case TextualElement textual:
                json["text"] = textual.Text;
                break;
            case LinkElement link:
                json["url"] = link.Url;
                json["title"] = link.Title;
                break;
            case AttachmentElement attachment:
                json["asset"] = attachment.Asset;
                json["fileName"] = attachment.FileName;
                break;
            case AssetElement asset:
                json["asset"] = asset.Asset;
                break;
            case FileReferenceElement fileReference:
                json["path"] = fileReference.Path;
                break;
            case DividerElement:
                break;
            default:
                throw new InvalidOperationException($"No serializer for element type {element.GetType().Name}.");
        }
        return json;
    }

    private static string NewUniqueId(HashSet<string> seen)
    {
        string id;
        do id = Ids.New(); while (!seen.Add(id));
        return id;
    }

    private static Element ReadElement(JsonObject json, string id)
    {
        var type = ReadString(json, "type")
            ?? throw new QuireFormatException("An element has no type.");
        string Text(string name) => ReadString(json, name) ?? "";

        return type switch
        {
            ElementTypes.Text => new TextElement { Id = id, Text = Text("text") },
            ElementTypes.Markdown => new MarkdownElement { Id = id, Text = Text("text") },
            ElementTypes.Code => new CodeElement { Id = id, Text = Text("text"), Language = Text("language") },
            ElementTypes.Link => new LinkElement { Id = id, Url = Text("url"), Title = Text("title") },
            ElementTypes.Image => new ImageElement { Id = id, Asset = Text("asset") },
            ElementTypes.Gif => new GifElement { Id = id, Asset = Text("asset") },
            ElementTypes.Attachment => new AttachmentElement { Id = id, Asset = Text("asset"), FileName = Text("fileName") },
            ElementTypes.FileReference => new FileReferenceElement { Id = id, Path = Text("path") },
            ElementTypes.Divider => new DividerElement { Id = id },
            _ => new UnknownElement { Id = id, OriginalType = type, Raw = WithId((JsonObject)json.DeepClone(), id) },
        };
    }

    private static JsonObject WithId(JsonObject json, string id)
    {
        json["id"] = id;
        return json;
    }

    private static string? ReadString(JsonObject json, string name) =>
        json[name] is JsonValue value && value.TryGetValue<string>(out var result) ? result : null;

    private static int? ReadInt(JsonObject json, string name) =>
        json[name] is JsonValue value && value.TryGetValue<int>(out var result) ? result : null;
}
