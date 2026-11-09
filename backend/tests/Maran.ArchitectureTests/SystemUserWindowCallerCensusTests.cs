using System.Text.RegularExpressions;

namespace Maran.ArchitectureTests;

/// <summary>
/// The census over callers of the unscoped account window
/// <c>IAccountDirectory.FindSystemUserAsync</c> (issue #379): the only production code allowed to
/// call it is unattended host work with no principal (certificate renewal and the hourly site log
/// maintenance), so a new caller on a request path fails here by name.
/// </summary>
/// <remarks>
/// <para>
/// The window answers one account's system user name by id with no tenant scope, so its safety is the
/// short list of who calls it. A list kept only in a document is stale within a release; this test is
/// the mechanism, and widening <see cref="ExpectedCallers"/> is a visible edit that a reviewer sees.
/// </para>
/// <para>
/// <b>UNOBSERVED HERE:</b> this is a text scan over <c>backend/src</c>, not IL inspection. It sees a
/// written <c>.FindSystemUserAsync(</c> call outside comment lines. It does not see a call through an
/// alias or a delegate made from the method group, <c>dynamic</c>, reflection, or one assembled in a
/// string, and it does not see callers outside <c>backend/src</c>. The positive control proves the
/// scan can hit; it cannot prove those routes are absent.
/// </para>
/// </remarks>
public sealed class SystemUserWindowCallerCensusTests
{
    /// <summary>
    /// The production files allowed to call the window, relative to <c>backend/src</c>, with forward
    /// slashes: unattended work that holds the account id from its own row and runs with no principal.
    /// </summary>
    private static readonly string[] ExpectedCallers =
    [
        // Scheduled certificate renewal: no principal, the id is the certificate's own AccountId.
        "Maran.Modules/Ssl/Jobs/CertificateRenewalHandler.cs",

        // Hourly log retention, and the message a retention change publishes: no principal, the id is the
        // loaded site's own AccountId, and the account's system user only addresses that account's own files.
        "Maran.Modules/Sites/Services/SiteLogMaintainer.cs",
    ];

    /// <summary>A call written as code, never the declaration, which has no dot before the name.</summary>
    private static readonly Regex CallSite = new(
        @"\.FindSystemUserAsync\s*\(",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>The only production callers of the window are unattended host work.</summary>
    [Fact]
    public void The_only_production_callers_of_the_system_user_window_are_unattended_host_work()
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
            "var user = await _accounts.FindSystemUserAsync(command.AccountId, ct);");
        var commented = new SourceFile(
            "Maran.Modules/Sites/Commands/Planted/CommentedHandler.cs",
            "    /// Never call _accounts.FindSystemUserAsync(id) from here.\n"
            + "    // _accounts.FindSystemUserAsync(id)");
        var declaration = new SourceFile(
            "Maran.Sdk/Interfaces/IPlanted.cs",
            "Task<AccountSystemUser?> FindSystemUserAsync(Guid accountId, CancellationToken ct);");

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
