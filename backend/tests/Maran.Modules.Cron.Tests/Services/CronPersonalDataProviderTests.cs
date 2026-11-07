using Maran.Modules.Cron.Domain.Entities;
using Maran.Modules.Cron.Domain.ValueObjects;
using Maran.Modules.Cron.Services;
using Maran.Modules.Cron.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Cron.Tests.Services;

/// <summary>
/// The cron personal-data export: the run metadata of the named account's jobs, nobody else's, and never
/// the text a job printed.
/// </summary>
public sealed class CronPersonalDataProviderTests
{
    /// <summary>Fragments no exported field name may contain: the vocabulary of secrets and of output text.</summary>
    private static readonly string[] ForbiddenFragments =
    [
        "password", "hash", "secret", "token", "privatekey", "output", "stdout", "stderr", "log", "sha256", "digest",
    ];

    /// <summary>What a job printed to standard output: stands in for a secret the program echoed.</summary>
    private const string PlantedStdout = "PLANTED-STDOUT-9f3a";

    /// <summary>What a job printed to standard error: stands in for a secret the program echoed.</summary>
    private const string PlantedStderr = "PLANTED-STDERR-4c1d";

    /// <summary>The fields a run record may carry, and no others.</summary>
    private static readonly string[] AllowedFields =
    [
        "id", "entryId", "startedAt", "durationMilliseconds", "endKind", "exitCode",
    ];

    /// <summary>The export holds exactly the allow-listed fields of the named account's runs.</summary>
    [Fact]
    public async Task The_export_holds_exactly_the_allow_listed_fields_of_the_named_accounts_runs()
    {
        var mine = Guid.NewGuid();
        await using var context = InMemoryCronDatabase.Open(Guid.NewGuid().ToString(), FakeCurrentUser.Admin());
        context.CronRuns.Add(Run(mine));
        context.CronRuns.Add(Run(Guid.NewGuid()));
        await context.SaveChangesAsync();

        var sections = await new CronPersonalDataProvider(context)
            .ExportAsync(new PersonalDataSubject(PersonalDataSubjectKind.Account, mine), CancellationToken.None);

        var section = Assert.Single(sections);
        var record = Assert.Single(section.Records);
        Assert.Equal(AllowedFields.Order(StringComparer.Ordinal), record.Fields.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("0", record.Fields["exitCode"]);

        var names = sections.SelectMany(section => { return section.Records; })
            .SelectMany(record => { return record.Fields.Keys; })
            .ToList();
        Assert.NotEmpty(names);
        Assert.All(names, name =>
        {
            Assert.DoesNotContain(
                ForbiddenFragments,
                fragment => { return name.Contains(fragment, StringComparison.OrdinalIgnoreCase); });
        });
    }

    /// <summary>What a job printed is stored on the row and is not in the export.</summary>
    /// <remarks>
    /// The positive control is the pair of first assertions: the stored row does hold the planted text,
    /// so the absence asserted after it is a property of the provider and not of an empty row.
    /// </remarks>
    [Fact]
    public async Task What_a_job_printed_is_not_exported()
    {
        var mine = Guid.NewGuid();
        await using var context = InMemoryCronDatabase.Open(Guid.NewGuid().ToString(), FakeCurrentUser.Admin());
        context.CronRuns.Add(Run(mine));
        await context.SaveChangesAsync();
        var stored = context.CronRuns.Single();
        Assert.Equal(PlantedStdout, stored.Stdout);
        Assert.Equal(PlantedStderr, stored.Stderr);

        var sections = await new CronPersonalDataProvider(context)
            .ExportAsync(new PersonalDataSubject(PersonalDataSubjectKind.Account, mine), CancellationToken.None);

        var values = sections.SelectMany(section => { return section.Records; })
            .SelectMany(record => { return record.Fields.Values; })
            .ToList();
        Assert.NotEmpty(values);
        Assert.DoesNotContain(
            values,
            value => { return value is not null && value.Contains("PLANTED", StringComparison.Ordinal); });
    }

    /// <summary>A user subject gets an empty list, because this module holds no row about a login.</summary>
    [Fact]
    public async Task A_user_subject_gets_an_empty_list()
    {
        await using var context = InMemoryCronDatabase.Open(Guid.NewGuid().ToString(), FakeCurrentUser.Admin());

        var sections = await new CronPersonalDataProvider(context)
            .ExportAsync(new PersonalDataSubject(PersonalDataSubjectKind.User, Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(sections);
    }

    /// <summary>Builds a finished run of one job carrying planted output.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <returns>The run.</returns>
    private static CronRun Run(Guid accountId)
    {
        return new CronRun(
            Guid.NewGuid(),
            accountId,
            "entry-1",
            DateTimeOffset.UnixEpoch,
            1500,
            new CronRunExited(0),
            PlantedStdout,
            PlantedStderr);
    }
}
