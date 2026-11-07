using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Domain.ValueObjects;
using Maran.Modules.Backups.Services;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Backups.Tests.Services;

/// <summary>
/// The backups personal-data export: the backups and schedules of the named account, nobody else's, and
/// never the artifact digest.
/// </summary>
public sealed class BackupsPersonalDataProviderTests
{
    /// <summary>Fragments no exported field name may contain: the vocabulary of secrets and of output text.</summary>
    private static readonly string[] ForbiddenFragments =
    [
        "password", "hash", "secret", "token", "privatekey", "output", "stdout", "stderr", "log", "sha256", "digest",
    ];

    /// <summary>The digest a completed backup carries: stands in for a value this export never holds.</summary>
    private const string PlantedDigest = "PLANTED-DIGEST-5d8c";

    /// <summary>The fields a backup record may carry, and no others.</summary>
    private static readonly string[] BackupFields =
    [
        "id", "kind", "status", "sizeBytes", "startedAt", "finishedAt",
    ];

    /// <summary>The fields a schedule record may carry, and no others.</summary>
    private static readonly string[] ScheduleFields = ["id", "retainCount", "enabledAt", "lastRunAt"];

    /// <summary>The export holds exactly the allow-listed fields of the account's backups and schedules.</summary>
    [Fact]
    public async Task The_export_holds_exactly_the_allow_listed_fields_of_the_named_accounts_backups_and_schedules()
    {
        var mine = Guid.NewGuid();
        await using var context = BackupsTestContext.Create(FakeCurrentUser.Admin());
        context.Backups.Add(CompletedBackup(mine));
        context.Backups.Add(CompletedBackup(Guid.NewGuid()));
        context.BackupSchedules.Add(new BackupSchedule(Guid.NewGuid(), mine, null, new DailyBackupCadence(3), 7));
        context.BackupSchedules.Add(
            new BackupSchedule(Guid.NewGuid(), Guid.NewGuid(), null, new DailyBackupCadence(3), 7));
        await context.SaveChangesAsync();

        var sections = await new BackupsPersonalDataProvider(context)
            .ExportAsync(new PersonalDataSubject(PersonalDataSubjectKind.Account, mine), CancellationToken.None);

        var backups = Assert.Single(sections, section => { return section.Name == "backups"; });
        var backup = Assert.Single(backups.Records);
        Assert.Equal(BackupFields.Order(StringComparer.Ordinal), backup.Fields.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("Completed", backup.Fields["status"]);

        var schedules = Assert.Single(sections, section => { return section.Name == "schedules"; });
        var schedule = Assert.Single(schedules.Records);
        Assert.Equal(ScheduleFields.Order(StringComparer.Ordinal), schedule.Fields.Keys.Order(StringComparer.Ordinal));

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

    /// <summary>The artifact digest is stored on the row and is not in the export.</summary>
    /// <remarks>
    /// The positive control is the first assertion: the stored row does hold the planted digest, so the
    /// absence asserted after it is a property of the provider and not of an empty row.
    /// </remarks>
    [Fact]
    public async Task The_artifact_digest_is_not_exported()
    {
        var mine = Guid.NewGuid();
        await using var context = BackupsTestContext.Create(FakeCurrentUser.Admin());
        context.Backups.Add(CompletedBackup(mine));
        await context.SaveChangesAsync();
        Assert.Equal(PlantedDigest, context.Backups.Single().ArtifactSha256);

        var sections = await new BackupsPersonalDataProvider(context)
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
        await using var context = BackupsTestContext.Create(FakeCurrentUser.Admin());

        var sections = await new BackupsPersonalDataProvider(context)
            .ExportAsync(new PersonalDataSubject(PersonalDataSubjectKind.User, Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(sections);
    }

    /// <summary>Builds a completed backup of one account carrying a planted digest.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <returns>The backup.</returns>
    private static Backup CompletedBackup(Guid accountId)
    {
        var backup = new Backup(Guid.NewGuid(), accountId, null, BackupKind.Manual, DateTimeOffset.UnixEpoch);
        backup.Completed(2048, PlantedDigest, 1, DateTimeOffset.UnixEpoch.AddMinutes(1));

        return backup;
    }
}
