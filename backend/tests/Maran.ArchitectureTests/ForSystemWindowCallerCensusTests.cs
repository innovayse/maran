using System.Text.RegularExpressions;

namespace Maran.ArchitectureTests;

/// <summary>
/// The census over callers of the unscoped account snapshot window
/// <c>IAccountDirectory.FindForSystemAsync</c> (issue #518): the only production code allowed to call it
/// are the Mail module's local submission publisher and the Sites module's plan-limits reader, system actions
/// raised after the owning module authorised them, so a new caller on a request path fails here by name.
/// </summary>
/// <remarks>
/// <para>
/// The window answers one account's whole snapshot by id with no tenant scope, so its safety is the
/// short list of who calls it. A list kept only in a document is stale within a release; this test is
/// the mechanism, and widening <see cref="ExpectedCallers"/> is a visible edit that a reviewer sees.
/// </para>
/// <para>
/// <b>UNOBSERVED HERE:</b> this is a text scan over <c>backend/src</c>, not IL inspection. It sees a
/// written <c>.FindForSystemAsync(</c> call outside comment lines. It does not see a call through an
/// alias or a delegate made from the method group, <c>dynamic</c>, reflection, or one assembled in a
/// string, and it does not see callers outside <c>backend/src</c>. The positive control proves the
/// scan can hit; it cannot prove those routes are absent.
/// </para>
/// </remarks>
public sealed class ForSystemWindowCallerCensusTests
{
    /// <summary>
    /// The production files allowed to call the window, relative to <c>backend/src</c>, with forward
    /// slashes: system actions that hold the account id from an event and use the snapshot without returning it.
    /// </summary>
    private static readonly string[] ExpectedCallers =
    [
        // Binding an account's local mail submission on creation, resumption and plan change: the id comes
        // from the event, the figures are used by the push and never returned.
        "Maran.Modules/Mail/Services/MailLocalSubmissionPublisher.cs",
        // Reading the plan's PHP bounds a vhost's request body limit follows, for the account cascades and the
        // site directory the unattended certificate renewal reads through: the id comes from a row, the
        // bounds decide a vhost and are never returned (issue #556).
        "Maran.Modules/Sites/Services/SitePlanLimits.cs",
    ];

    /// <summary>A call written as code, never the declaration, which has no dot before the name.</summary>
    private static readonly Regex CallSite = new(
        @"\.FindForSystemAsync\s*\(",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>The only production callers of the window are the listed system actions.</summary>
    [Fact]
    public void The_only_production_callers_of_the_system_snapshot_window_are_the_listed_system_actions()
    {
        var sources = ProductionSources();

        var callers = CallerFiles(sources);

        Assert.Equal(
            ExpectedCallers.Order(StringComparer.Ordinal).ToList(),
            callers.Order(StringComparer.Ordinal).ToList());
    }

    /// <summary>The scan reads a real tree: it finds files, and finds the expected caller's file among them.</summary>
    /// <remarks>
    /// The vacuity guard on the axis that can go blind: a wrong root or a filter that skips
    /// everything would make the census above compare an empty set against a non-empty one and fail,
    /// but a scan that read the tree and lost the call would be indistinguishable from "no callers"
    /// without proof that the caller's file was read at all.
    /// </remarks>
    [Fact]
    public void The_scan_reads_the_expected_callers_file()
    {
        var sources = ProductionSources();

        foreach (var expected in ExpectedCallers)
        {
            Assert.Contains(sources, source =>
            {
                return source.RelativePath == expected;
            });
        }
    }

    /// <summary>The positive control: a planted call in a request-path file is reported, a comment is not.</summary>
    [Fact]
    public void A_planted_call_is_reported_and_a_commented_one_is_not()
    {
        var planted = new SourceFile(
            "Maran.Modules/Sites/Commands/Planted/PlantedHandler.cs",
            "var user = await _accounts.FindForSystemAsync(command.AccountId, ct);");
        var commented = new SourceFile(
            "Maran.Modules/Sites/Commands/Planted/CommentedHandler.cs",
            "    /// Never call _accounts.FindForSystemAsync(id) from here.\n"
            + "    // _accounts.FindForSystemAsync(id)");
        var declaration = new SourceFile(
            "Maran.Sdk/Interfaces/IPlanted.cs",
            "Task<AccountSnapshot?> FindForSystemAsync(Guid accountId, CancellationToken ct);");

        var callers = CallerFiles([planted, commented, declaration]);

        Assert.Equal([planted.RelativePath], callers);
    }

    /// <summary>Reads every production C# file under <c>backend/src</c>, skipping build output and generated code.</summary>
    /// <returns>The files with their paths below <c>backend/src</c>.</returns>
    private static List<SourceFile> ProductionSources()
    {
        var root = Path.Combine(SpaLocaleKeys.RepositoryRoot(), "backend", "src");
        Assert.True(Directory.Exists(root), $"{root} was not found.");

        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(path =>
            {
                return Path.GetRelativePath(root, path).Replace('\\', '/');
            })
            .Where(relative =>
            {
                var segments = relative.Split('/');
                return !segments.Contains("bin") && !segments.Contains("obj") && !segments.Contains("Generated");
            })
            .Select(relative =>
            {
                return new SourceFile(relative, File.ReadAllText(Path.Combine(root, relative)));
            })
            .ToList();
    }

    /// <summary>Lists the files holding a call to the window, ignoring comment lines.</summary>
    /// <param name="sources">The files to scan.</param>
    /// <returns>The relative paths of the files with at least one call.</returns>
    private static List<string> CallerFiles(IEnumerable<SourceFile> sources)
    {
        return sources
            .Where(source =>
            {
                return source.Text
                    .Split('\n')
                    .Any(line =>
                    {
                        return !line.TrimStart().StartsWith("//", StringComparison.Ordinal) && CallSite.IsMatch(line);
                    });
            })
            .Select(source =>
            {
                return source.RelativePath;
            })
            .ToList();
    }

    /// <summary>The files of one source tree the census reads.</summary>
    /// <param name="RelativePath">The path below <c>backend/src</c>, with forward slashes.</param>
    /// <param name="Text">The file's text.</param>
    private sealed record SourceFile(string RelativePath, string Text);
}
