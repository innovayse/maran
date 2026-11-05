using System.Runtime.CompilerServices;
using System.Text;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Compares a live response with its committed golden file and says, line by line, what moved. A shape
/// change fails the comparison with the lines that changed; the golden is rewritten only when a developer
/// sets <c>UPDATE_CONTRACT_SNAPSHOTS=1</c> on their own machine, never in CI.
/// </summary>
/// <remarks>
/// <b>Where the goldens are read from.</b> From the source tree beside the test classes, located through
/// the compiler's record of this file's own path, and not from copies in the build output: the update
/// switch has to write somewhere a developer can review and commit, and a copy in <c>bin/</c> is not
/// that. The consequence, stated so it is not discovered: the tests must run on the checkout that built
/// them, which is how every job in this repository runs them.
/// </remarks>
public static class ContractGolden
{
    /// <summary>The environment variable that makes <see cref="Verify"/> rewrite goldens instead of comparing.</summary>
    public const string UpdateVariable = "UPDATE_CONTRACT_SNAPSHOTS";

    /// <summary>Environment variables a CI runner sets; rewriting goldens there would make every run green.</summary>
    private static readonly string[] CiVariables = ["CI", "GITHUB_ACTIONS", "TF_BUILD"];

    /// <summary>Compares two normalised documents.</summary>
    /// <param name="expected">The golden's text.</param>
    /// <param name="actual">The live response, normalised.</param>
    /// <returns>Null when they are the same; otherwise the lines only the golden has (<c>-</c>) and only the live response has (<c>+</c>).</returns>
    public static string? Diff(string expected, string actual)
    {
        var expectedLines = Lines(expected);
        var actualLines = Lines(actual);

        if (expectedLines.SequenceEqual(actualLines, StringComparer.Ordinal))
        {
            return null;
        }

        var report = new StringBuilder();

        foreach (var line in Unmatched(expectedLines, actualLines))
        {
            report.Append("- ").AppendLine(line);
        }

        foreach (var line in Unmatched(actualLines, expectedLines))
        {
            report.Append("+ ").AppendLine(line);
        }

        return report.Length == 0 ? "The same lines in a different order." : report.ToString().TrimEnd();
    }

    /// <summary>Compares a live response with the golden of that name, or rewrites the golden when asked to.</summary>
    /// <param name="name">The golden's file name without its extension.</param>
    /// <param name="liveJson">The response body exactly as the server wrote it.</param>
    /// <returns>Null when it matches (or was rewritten); otherwise a readable description of the mismatch.</returns>
    /// <exception cref="InvalidOperationException">Rewriting was asked for on a CI runner.</exception>
    public static string? Verify(string name, string liveJson)
    {
        if (Environment.GetEnvironmentVariable(UpdateVariable) != "1")
        {
            return Compare(name, liveJson);
        }

        if (CiVariables.Any(variable => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable))))
        {
            throw new InvalidOperationException(
                $"{UpdateVariable}=1 is refused on a CI runner: a job that rewrites its own goldens enforces nothing.");
        }

        File.WriteAllText(PathOf(name), JsonShapeNormaliser.Normalise(liveJson) + "\n");

        return null;
    }

    /// <summary>
    /// Compares a live response with the golden of that name and never writes: the comparison alone, which
    /// the comparer's own tests call so that running them with the update switch on cannot rewrite a golden.
    /// </summary>
    /// <param name="name">The golden's file name without its extension.</param>
    /// <param name="liveJson">The response body exactly as the server wrote it.</param>
    /// <returns>Null when it matches; otherwise a readable description of the mismatch.</returns>
    public static string? Compare(string name, string liveJson)
    {
        var actual = JsonShapeNormaliser.Normalise(liveJson);
        var path = PathOf(name);

        if (!File.Exists(path))
        {
            return $"{name}: there is no golden at {path}. Run the tests locally with {UpdateVariable}=1, review the "
                + $"file it writes and commit it. The live response was:\n{actual}";
        }

        var difference = Diff(File.ReadAllText(path).Replace("\r\n", "\n"), actual);

        return difference is null
            ? null
            : $"{name}: the response no longer matches {path}.\n{difference}\n"
                + $"If the change is intended, rerun locally with {UpdateVariable}=1 and commit the golden's diff.";
    }

    /// <summary>The path of the golden with a name.</summary>
    /// <param name="name">The golden's file name without its extension.</param>
    /// <returns>The absolute path.</returns>
    private static string PathOf(string name)
    {
        return Path.Combine(GoldenFolder(), name + ".json");
    }

    /// <summary>The folder the goldens live in, beside the snapshot test classes.</summary>
    /// <param name="here">Filled in by the compiler with this file's path; never passed.</param>
    /// <returns>The absolute path of <c>ContractSnapshots/</c>.</returns>
    private static string GoldenFolder([CallerFilePath] string here = "")
    {
        var project = Path.GetDirectoryName(Path.GetDirectoryName(here))!;

        return Path.Combine(project, "ContractSnapshots");
    }

    /// <summary>Splits a document into trimmed-end lines, ignoring trailing blank lines.</summary>
    /// <param name="text">The document.</param>
    /// <returns>Its lines.</returns>
    private static List<string> Lines(string text)
    {
        return text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Select(line => line.TrimEnd()).ToList();
    }

    /// <summary>The lines of <paramref name="from"/> that have no counterpart in <paramref name="other"/>, counting duplicates.</summary>
    /// <param name="from">The lines to look through.</param>
    /// <param name="other">The lines they are matched against.</param>
    /// <returns>The unmatched lines, in their original order.</returns>
    private static List<string> Unmatched(List<string> from, List<string> other)
    {
        var available = other.GroupBy(line => line, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var unmatched = new List<string>();

        foreach (var line in from)
        {
            if (available.TryGetValue(line, out var left) && left > 0)
            {
                available[line] = left - 1;
            }
            else
            {
                unmatched.Add(line);
            }
        }

        return unmatched;
    }
}
