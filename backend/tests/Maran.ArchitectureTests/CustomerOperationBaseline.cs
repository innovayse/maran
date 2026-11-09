namespace Maran.ArchitectureTests;

/// <summary>
/// The committed customer self-service matrix, as read from <c>Baselines/customer-operations.txt</c>.
/// </summary>
/// <remarks>
/// One line per operation, fields separated by a tab: <c>METHOD route</c>, the module, then either the
/// integration test class holding the tenant-isolation case or <c>-</c> followed by a fourth field, the
/// reason the operation reaches no other account's resource. <c>#</c> starts a comment. A malformed or
/// repeated line is refused by naming it, so a hand edit that breaks the file cannot read as an empty matrix.
/// </remarks>
public static class CustomerOperationBaseline
{
    /// <summary>The file this matrix is read from, relative to the test binary.</summary>
    public const string RelativePath = "Baselines/customer-operations.txt";

    /// <summary>The marker in the isolation column of an operation that needs no isolation case.</summary>
    private const string NoIsolation = "-";

    /// <summary>Reads the committed matrix next to the test binary.</summary>
    /// <returns>The parsed entries.</returns>
    public static List<CustomerOperationEntry> Load()
    {
        return Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, RelativePath)));
    }

    /// <summary>Parses the text of a matrix.</summary>
    /// <param name="text">The file's text.</param>
    /// <returns>The entries in file order.</returns>
    /// <exception cref="FormatException">A line is malformed or repeats an earlier operation; the message names it.</exception>
    public static List<CustomerOperationEntry> Parse(string text)
    {
        var entries = new List<CustomerOperationEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var number = 0;

        foreach (var raw in text.Split('\n'))
        {
            number++;
            var line = raw.TrimEnd('\r');
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var fields = line.Split('\t');
            var bad = new FormatException($"{RelativePath} line {number} is malformed or repeated: '{line}'.");
            var isolation = fields.Length > 2 ? fields[2] : string.Empty;
            var exempt = isolation == NoIsolation;

            if (fields.Length != (exempt ? 4 : 3) || fields.Any(field => { return field.Trim().Length == 0; }))
            {
                throw bad;
            }

            if (!seen.Add(fields[0]))
            {
                throw bad;
            }

            entries.Add(new CustomerOperationEntry(
                fields[0],
                fields[1],
                exempt ? null : isolation,
                exempt ? fields[3] : string.Empty));
        }

        return entries;
    }
}
