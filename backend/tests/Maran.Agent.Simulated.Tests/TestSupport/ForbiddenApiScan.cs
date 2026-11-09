using System.Text.RegularExpressions;

namespace Maran.Agent.Simulated.Tests.TestSupport;

/// <summary>
/// Reads C# source for the few APIs a simulated agent must never use: spawning a process, touching a
/// file system, opening a socket. Comments and string literals are removed first, so a doc comment that
/// explains why nothing here opens a socket is not mistaken for code that does.
/// </summary>
public static partial class ForbiddenApiScan
{
    /// <summary>Each rule's name and the matcher that finds what it forbids.</summary>
    private static readonly (string Name, Regex Pattern)[] Rules =
    [
        ("process", ProcessPattern()),
        ("file system", FileSystemPattern()),
        ("network", NetworkPattern()),
    ];

    /// <summary>Names the forbidden APIs <paramref name="source"/> uses.</summary>
    /// <param name="source">The text of one C# file.</param>
    /// <returns>The name of every rule the code breaks, once each; empty for clean code.</returns>
    public static IReadOnlyList<string> Violations(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var withoutStrings = StringLiteral().Replace(source, "\"\"");
        var code = LineComment().Replace(BlockComment().Replace(withoutStrings, string.Empty), string.Empty);

        return Rules
            .Where(rule => { return rule.Pattern.IsMatch(code); })
            .Select(rule => { return rule.Name; })
            .ToList();
    }

    /// <summary>The process rule: starting or naming a child process.</summary>
    /// <returns>The generated matcher.</returns>
    [GeneratedRegex(
        @"\bSystem\.Diagnostics\.Process\b|\bProcess\.Start\b|\bProcessStartInfo\b|\bnew\s+Process\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex ProcessPattern();

    /// <summary>The file-system rule: reading, writing or enumerating files and directories.</summary>
    /// <returns>The generated matcher.</returns>
    [GeneratedRegex(
        @"\bSystem\.IO\.(File|Directory|FileStream|StreamWriter|StreamReader|DriveInfo)\b"
        + @"|\bFile\.(Read|Write|Open|Append|Create|Delete|Copy|Move|Exists|Replace)\w*\s*\("
        + @"|\bDirectory\.(Create|Delete|Exists|Enumerate|Get|Move)\w*\s*\("
        + @"|\bnew\s+(FileStream|StreamWriter|StreamReader|FileInfo|DirectoryInfo)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex FileSystemPattern();

    /// <summary>The network rule: opening a socket, an HTTP client or a name lookup.</summary>
    /// <returns>The generated matcher.</returns>
    [GeneratedRegex(
        @"\b(HttpClient|HttpMessageHandler|SocketsHttpHandler|Socket|TcpClient|TcpListener|UdpClient"
        + @"|NetworkStream|WebClient|HttpWebRequest|ClientWebSocket)\b|\bDns\.\w+\s*\(",
        RegexOptions.CultureInvariant)]
    private static partial Regex NetworkPattern();

    /// <summary>Matches an ordinary string literal, escapes included.</summary>
    /// <returns>The generated matcher.</returns>
    [GeneratedRegex("\"(?:[^\"\\\\\\r\\n]|\\\\.)*\"", RegexOptions.CultureInvariant)]
    private static partial Regex StringLiteral();

    /// <summary>Matches a block comment, across lines.</summary>
    /// <returns>The generated matcher.</returns>
    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex BlockComment();

    /// <summary>Matches a line comment, doc comments included.</summary>
    /// <returns>The generated matcher.</returns>
    [GeneratedRegex(@"//[^\r\n]*", RegexOptions.CultureInvariant)]
    private static partial Regex LineComment();
}
