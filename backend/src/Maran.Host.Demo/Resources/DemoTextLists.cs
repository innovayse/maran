namespace Maran.Host.Demo.Resources;

/// <summary>
/// Reads the data the demo's text screen is built from: the blocked terms of every language, the hosts a link
/// may name and the documentation namespaces an <c>http</c> or <c>https</c> link may name besides. The words
/// are data in <c>Resources/DemoBlockedTerms.&lt;language&gt;.txt</c>, <c>Resources/DemoAllowedHosts.txt</c> and
/// <c>Resources/DemoDocumentationHosts.txt</c>, embedded in the assembly, so extending a list never touches code.
/// </summary>
/// <remarks>
/// A list that came back empty would make the screen accept everything while looking installed, so an empty
/// read is a failure at start-up and not an empty answer.
/// </remarks>
public static class DemoTextLists
{
    /// <summary>The embedded name of the allow-list file.</summary>
    public const string AllowedHostsResource = "Maran.Host.Demo.Resources.DemoAllowedHosts.txt";

    /// <summary>The embedded name of the documentation-host file.</summary>
    public const string DocumentationHostsResource = "Maran.Host.Demo.Resources.DemoDocumentationHosts.txt";

    /// <summary>The prefix of the embedded names of the per-language blocked-term files.</summary>
    public const string BlockedTermsPrefix = "Maran.Host.Demo.Resources.DemoBlockedTerms.";

    /// <summary>The suffix of the embedded names of the blocked-term files.</summary>
    private const string TermsSuffix = ".txt";

    /// <summary>The embedded names of the blocked-term files, one per language, in name order.</summary>
    /// <returns>The resource names.</returns>
    public static IReadOnlyList<string> BlockedTermResources()
    {
        return typeof(DemoTextLists).Assembly.GetManifestResourceNames()
            .Where(name =>
            {
                return name.StartsWith(BlockedTermsPrefix, StringComparison.Ordinal)
                    && name.EndsWith(TermsSuffix, StringComparison.Ordinal);
            })
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Every blocked term of every language.</summary>
    /// <returns>The terms as written in the files.</returns>
    /// <exception cref="InvalidOperationException">No term was found, so the screen would refuse nothing.</exception>
    public static IReadOnlyList<string> BlockedTerms()
    {
        var terms = BlockedTermResources().SelectMany(ReadLines).ToList();
        if (terms.Count == 0)
        {
            throw new InvalidOperationException("The demo's blocked-term lists are empty or not embedded.");
        }

        return terms;
    }

    /// <summary>The hosts a link in visitor text may name.</summary>
    /// <returns>The hosts as written in the file.</returns>
    /// <exception cref="InvalidOperationException">The file is missing.</exception>
    public static IReadOnlyList<string> AllowedHosts()
    {
        return ReadLines(AllowedHostsResource).ToList();
    }

    /// <summary>The documentation namespaces an <c>http</c> or <c>https</c> link in visitor text may name.</summary>
    /// <returns>The hosts as written in the file.</returns>
    /// <exception cref="InvalidOperationException">The file is missing.</exception>
    public static IReadOnlyList<string> DocumentationHosts()
    {
        return ReadLines(DocumentationHostsResource).ToList();
    }

    /// <summary>Reads the entries of one embedded list: one per line, blank lines and <c>#</c> lines skipped.</summary>
    /// <param name="resourceName">The embedded name.</param>
    /// <returns>The entries, trimmed.</returns>
    private static IEnumerable<string> ReadLines(string resourceName)
    {
        using var stream = typeof(DemoTextLists).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The embedded demo list {resourceName} is missing.");
        using var reader = new StreamReader(stream);
        var entries = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            var entry = line.Trim();
            if (entry.Length > 0 && !entry.StartsWith('#'))
            {
                entries.Add(entry);
            }
        }

        return entries;
    }
}
