using System.Text.RegularExpressions;

namespace Quire.Editing;

public enum TokenKind
{
    Plain,
    Keyword,
    String,
    Comment,
    Number,
}

public readonly record struct Token(string Text, TokenKind Kind);

/// <summary>
/// Basic, forgiving syntax highlighting: comments, strings, numbers and keywords for common
/// language families. It never fails on malformed code; anything unrecognised is plain text.
/// </summary>
public static class SyntaxTokenizer
{
    private sealed record Grammar(Regex Pattern, HashSet<string> Keywords);

    private static readonly Dictionary<string, Grammar> Grammars = new(StringComparer.OrdinalIgnoreCase);

    static SyntaxTokenizer()
    {
        var cLike = Build(@"//[^\n]*|/\*[\s\S]*?(?:\*/|$)", @"@""(?:[^""]|"""")*""|\$?""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])*'|`(?:\\.|[^`\\])*`");
        var hash = Build(@"#[^\n]*", @"""""""[\s\S]*?(?:""""""|$)|'''[\s\S]*?(?:'''|$)|[rbfu]?""(?:\\.|[^""\\\n])*""|[rbfu]?'(?:\\.|[^'\\\n])*'");
        var sql = Build(@"--[^\n]*|/\*[\s\S]*?(?:\*/|$)", @"'(?:''|[^'])*'|""(?:[^""])*""");
        var markup = Build(@"<!--[\s\S]*?(?:-->|$)", @"""[^""]*""|'[^']*'");
        var json = Build(@"(?!x)x", @"""(?:\\.|[^""\\\n])*""");

        Register(cLike, Words("""
            abstract as async await base bool break byte case catch char checked class const continue decimal default delegate do double
            else enum event explicit extern false finally fixed float for foreach goto if implicit in init int interface internal is lock long
            namespace new null object operator out override params private protected public readonly record ref required return sbyte sealed
            short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using var virtual
            void volatile when where while with yield get set value let function export import from extends implements package
            boolean undefined typeof instanceof debugger fn mut impl trait pub use mod crate self Self match loop type func go chan defer
            map range select fallthrough auto signed unsigned template typename nullptr constexpr include define elif endif
            """), "csharp", "cs", "c#", "java", "javascript", "js", "typescript", "ts", "tsx", "jsx", "c", "cpp", "c++", "h", "hpp",
            "go", "rust", "rs", "kotlin", "kt", "swift", "dart", "scala", "php");

        Register(hash, Words("""
            and as assert async await break class continue def del elif else except False finally for from global if import in is lambda
            None nonlocal not or pass raise return True try while with yield self print
            then fi do done esac case function local export echo exit unless until elsif end begin rescue ensure module require puts
            """), "python", "py", "bash", "sh", "shell", "zsh", "ruby", "rb", "yaml", "yml", "toml", "perl", "r", "makefile", "dockerfile");

        Register(hash, Words("""
            begin process end function param if else elseif foreach for while do switch return break continue throw try catch finally
            trap filter in true false null
            """), "powershell", "ps1", "pwsh");

        Register(sql, Words("""
            select from where and or not insert into values update set delete create table view index drop alter add join inner left right
            outer full on as group by order having limit offset union all distinct null is in exists between like case when then else end
            primary key foreign references default begin commit rollback transaction with desc asc count sum avg min max
            """, ignoreCase: true), "sql", "tsql", "mysql", "postgres", "sqlite");

        Register(markup, [], "html", "xml", "xaml", "svg", "csproj");
        Register(json, Words("true false null"), "json", "jsonc");
    }

    /// <summary>Splits code into tokens. Unknown or empty languages produce a single plain token.</summary>
    public static IReadOnlyList<Token> Tokenize(string code, string? language)
    {
        if (code.Length == 0)
            return [];
        if (string.IsNullOrWhiteSpace(language) || !Grammars.TryGetValue(language.Trim(), out var grammar))
            return [new Token(code, TokenKind.Plain)];

        try
        {
            return Merge(Scan(code, grammar));
        }
        catch (RegexMatchTimeoutException)
        {
            // Pathological input: show it unhighlighted rather than freeze the editor.
            return [new Token(code, TokenKind.Plain)];
        }
    }

    private static List<Token> Scan(string code, Grammar grammar)
    {
        var tokens = new List<Token>();
        var position = 0;
        foreach (Match match in grammar.Pattern.Matches(code))
        {
            if (match.Index > position)
                tokens.Add(new Token(code[position..match.Index], TokenKind.Plain));

            var kind = match.Groups["comment"].Success ? TokenKind.Comment
                : match.Groups["string"].Success ? TokenKind.String
                : match.Groups["number"].Success ? TokenKind.Number
                : grammar.Keywords.Contains(match.Value) ? TokenKind.Keyword
                : TokenKind.Plain;
            tokens.Add(new Token(match.Value, kind));
            position = match.Index + match.Length;
        }
        if (position < code.Length)
            tokens.Add(new Token(code[position..], TokenKind.Plain));
        return tokens;
    }

    public static bool IsKnownLanguage(string? language) =>
        !string.IsNullOrWhiteSpace(language) && Grammars.ContainsKey(language.Trim());

    private static Grammar Build(string comment, string @string) =>
        new(new Regex(
            $@"(?<comment>{comment})|(?<string>{@string})|(?<number>\b(?:0[xX][0-9a-fA-F_]+|\d[\d_]*(?:\.\d+)?(?:[eE][+-]?\d+)?)[a-zA-Z]*\b)|(?<word>[A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)), []);

    private static void Register(Grammar grammar, HashSet<string> keywords, params string[] names)
    {
        var withKeywords = grammar with { Keywords = keywords };
        foreach (var name in names)
            Grammars[name] = withKeywords;
    }

    private static HashSet<string> Words(string words, bool ignoreCase = false) =>
        new(words.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries),
            ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>Joins neighbouring plain tokens so the UI creates fewer runs.</summary>
    private static List<Token> Merge(List<Token> tokens)
    {
        var merged = new List<Token>(tokens.Count);
        foreach (var token in tokens)
        {
            if (merged.Count > 0 && merged[^1].Kind == token.Kind && token.Kind == TokenKind.Plain)
                merged[^1] = new Token(merged[^1].Text + token.Text, TokenKind.Plain);
            else
                merged.Add(token);
        }
        return merged;
    }
}
