using System.Text.Json.Nodes;

namespace Quire.Models;

/// <summary>One piece of content inside a cell. The <see cref="TypeName"/> is the discriminator stored in document.json.</summary>
public abstract class Element : ObservableObject
{
    public string Id { get; init; } = Ids.New();

    public abstract string TypeName { get; }

    /// <summary>Returns a copy with a new id.</summary>
    public abstract Element Duplicate();
}

/// <summary>Base for elements whose content is a single editable string.</summary>
public abstract class TextualElement : Element
{
    private string _text = "";

    public string Text
    {
        get => _text;
        set => SetField(ref _text, value ?? "");
    }
}

public sealed class TextElement : TextualElement
{
    public override string TypeName => ElementTypes.Text;
    public override Element Duplicate() => new TextElement { Text = Text };
}

public sealed class MarkdownElement : TextualElement
{
    public override string TypeName => ElementTypes.Markdown;
    public override Element Duplicate() => new MarkdownElement { Text = Text };
}

public sealed class CodeElement : TextualElement
{
    private string _language = "";

    public override string TypeName => ElementTypes.Code;

    public string Language
    {
        get => _language;
        set => SetField(ref _language, value ?? "");
    }

    public override Element Duplicate() => new CodeElement { Text = Text, Language = Language };
}

public sealed class LinkElement : Element
{
    private string _url = "";
    private string _title = "";

    public override string TypeName => ElementTypes.Link;

    public string Url
    {
        get => _url;
        set => SetField(ref _url, value ?? "");
    }

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value ?? "");
    }

    public override Element Duplicate() => new LinkElement { Url = Url, Title = Title };
}

/// <summary>Base for elements backed by a binary file stored in the document archive.</summary>
public abstract class AssetElement : Element
{
    private string _asset = "";

    /// <summary>Archive-relative path, for example <c>assets/image-001.png</c>. Changing it replaces the content in place.</summary>
    public string Asset
    {
        get => _asset;
        set => SetField(ref _asset, value ?? "");
    }
}

public sealed class ImageElement : AssetElement
{
    public override string TypeName => ElementTypes.Image;
    public override Element Duplicate() => new ImageElement { Asset = Asset };
}

public sealed class GifElement : AssetElement
{
    public override string TypeName => ElementTypes.Gif;
    public override Element Duplicate() => new GifElement { Asset = Asset };
}

/// <summary>A file copied into the document, so it travels with it.</summary>
public sealed class AttachmentElement : AssetElement
{
    public override string TypeName => ElementTypes.Attachment;

    /// <summary>The original file name shown to the user and used when the file is saved back out.</summary>
    public string FileName { get; init; } = "";

    public override Element Duplicate() => new AttachmentElement { Asset = Asset, FileName = FileName };
}

/// <summary>A path to a file outside the document. It may not exist on another machine.</summary>
public sealed class FileReferenceElement : Element
{
    private string _path = "";

    public override string TypeName => ElementTypes.FileReference;

    public string Path
    {
        get => _path;
        set => SetField(ref _path, value ?? "");
    }

    public override Element Duplicate() => new FileReferenceElement { Path = Path };
}

public sealed class DividerElement : Element
{
    public override string TypeName => ElementTypes.Divider;
    public override Element Duplicate() => new DividerElement();
}

/// <summary>An element type this version does not understand. Its JSON is kept so saving does not lose it.</summary>
public sealed class UnknownElement : Element
{
    public required string OriginalType { get; init; }
    public required JsonObject Raw { get; init; }

    public override string TypeName => OriginalType;
    public override Element Duplicate() => new UnknownElement { OriginalType = OriginalType, Raw = (JsonObject)Raw.DeepClone() };
}

public static class ElementTypes
{
    public const string Text = "text";
    public const string Markdown = "markdown";
    public const string Code = "code";
    public const string Link = "link";
    public const string Image = "image";
    public const string Gif = "gif";
    public const string Attachment = "attachment";
    public const string FileReference = "fileReference";
    public const string Divider = "divider";
}
