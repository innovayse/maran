using System.Text.RegularExpressions;

namespace Maran.ArchitectureTests;

/// <summary>
/// Reads the Marketplace module's sources for the two things the outbound gate forbids outside the
/// transport: touching an HTTP client, and running anything on its own schedule.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it looks for.</b> Whole-word type names, after comments are removed, so a doc comment that
/// explains why the transport owns the client does not count as a use, and neither does the
/// <c>AddHttpClient</c> registration call, whose name merely contains one.
/// </para>
/// <para>
/// UNOBSERVED HERE: a client reached through reflection or a name assembled at run time; a type in another
/// assembly that creates a client on this module's behalf (the Host's pipeline registration, which is
/// named in the architecture test); a <c>//</c> inside a string literal hides the rest of its line from the
/// comment remover's opposite number, which is why only a <c>//</c> not preceded by a colon is treated as a
/// comment. The planted sources in the tests prove the scanner finds the spellings it claims to.
/// </para>
/// </remarks>
public static class MarketplaceOutboundScanner
{
    /// <summary>Block comments, which may span lines.</summary>
    private static readonly Regex BlockComment = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    /// <summary>Line comments, not matching the <c>//</c> of a URL.</summary>
    private static readonly Regex LineComment = new(@"(?<!:)//[^\r\n]*", RegexOptions.CultureInvariant);

    /// <summary>The types by which code reaches the network.</summary>
    private static readonly Regex NetworkTypes = new(
        @"\b(HttpClient|IHttpClientFactory|HttpClientHandler|SocketsHttpHandler|HttpMessageInvoker|WebClient|WebRequest|TcpClient|UdpClient|ClientWebSocket)\b",
        RegexOptions.CultureInvariant);

    /// <summary>The types by which code starts work nobody asked for.</summary>
    private static readonly Regex BackgroundTypes = new(
        @"\b(IHostedService|BackgroundService|IHostedLifecycleService|PeriodicTimer|System\.Timers|IJobScheduler)\b|new\s+(System\.Threading\.)?Timer\s*\(",
        RegexOptions.CultureInvariant);

    /// <summary>Removes comments from C# source.</summary>
    /// <param name="source">The source text.</param>
    /// <returns>The source with block and line comments (documentation comments included) removed.</returns>
    public static string WithoutComments(string source)
    {
        return LineComment.Replace(BlockComment.Replace(source, string.Empty), string.Empty);
    }

    /// <summary>Names the network types a source text uses.</summary>
    /// <param name="source">The source text.</param>
    /// <returns>The distinct type names, ordered; empty when the text touches none.</returns>
    public static IReadOnlyList<string> NetworkUses(string source)
    {
        return Find(NetworkTypes, source);
    }

    /// <summary>Names the background-work types a source text uses.</summary>
    /// <param name="source">The source text.</param>
    /// <returns>The distinct matches, ordered; empty when the text starts no work of its own.</returns>
    public static IReadOnlyList<string> BackgroundUses(string source)
    {
        return Find(BackgroundTypes, source);
    }

    /// <summary>Lists the module's source files, build output excluded.</summary>
    /// <param name="moduleFolder">Absolute path of the module's folder.</param>
    /// <returns>Absolute paths, ordered.</returns>
    public static IReadOnlyList<string> SourceFiles(string moduleFolder)
    {
        var separator = Path.DirectorySeparatorChar;

        return Directory.EnumerateFiles(moduleFolder, "*.cs", SearchOption.AllDirectories)
            .Where(file =>
            {
                return !file.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                    && !file.Contains($"{separator}bin{separator}", StringComparison.Ordinal);
            })
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Finds what a pattern matches in the source once its comments are removed.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="source">The source text.</param>
    /// <returns>The distinct matches, ordered.</returns>
    private static List<string> Find(Regex pattern, string source)
    {
        return pattern.Matches(WithoutComments(source))
            .Select(match => { return match.Value; })
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}
