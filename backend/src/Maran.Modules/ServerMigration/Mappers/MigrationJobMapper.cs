using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Mappers;

/// <summary>Restates a job and its accounts as the shapes that go on the wire (issue #197, phase 2).</summary>
public static class MigrationJobMapper
{
    /// <summary>Maps a job with the count of its accounts in each state.</summary>
    /// <param name="job">The job.</param>
    /// <param name="stateCounts">How many accounts are in each state.</param>
    /// <param name="names">The module's display names, in the caller's language.</param>
    /// <returns>The wire shape.</returns>
    public static MigrationJobDto ToDto(
        MigrationJob job,
        IReadOnlyDictionary<MigrationAccountState, int> stateCounts,
        IStringLocalizer<DisplayNames> names)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(stateCounts);

        return new MigrationJobDto(
            job.Id,
            job.RemoteServerId,
            job.Name,
            job.State.ToString(),
            MigrationEnumNames.Of(names, job.State),
            job.Concurrency,
            job.BandwidthLimitKib,
            job.SourceUrl,
            stateCounts.Values.Sum(),
            stateCounts.ToDictionary(row =>
            {
                return row.Key.ToString();
            }, row =>
            {
                return row.Value;
            }),
            job.CreatedAt,
            job.StartedAt,
            job.FinishedAt);
    }

    /// <summary>Maps an account row; the grant and the issued secrets are reduced to whether they exist.</summary>
    /// <param name="account">The row.</param>
    /// <param name="failureName">The failure code as a sentence, or <c>null</c>.</param>
    /// <param name="names">The module's display names, in the caller's language.</param>
    /// <returns>The wire shape.</returns>
    public static MigrationJobAccountDto ToDto(
        MigrationJobAccount account,
        string? failureName,
        IStringLocalizer<DisplayNames> names)
    {
        ArgumentNullException.ThrowIfNull(account);

        var inventory = MigrationSectionMapper.ReadInventory(account);

        return new MigrationJobAccountDto(
            account.Id,
            account.SourceAccountName,
            account.SourceHome,
            account.SourceDiskBytes,
            inventory.Sites.Count,
            inventory.Databases.Count,
            account.CronEntryCount,
            account.MailboxCount,
            account.PlanId,
            account.TargetResellerId,
            account.OwnerEmail,
            account.OwnerLanguage,
            account.PrimaryDomain,
            !string.IsNullOrEmpty(account.PullGrant),
            account.State.ToString(),
            MigrationEnumNames.Of(names, account.State),
            account.Stage,
            account.FailureCode,
            failureName,
            account.TargetAccountId,
            account.SlicesCompleted,
            account.DatabasesLoaded,
            account.DumpBytes,
            account.IssuedSecretsJson is not null,
            account.SyncedAt,
            account.CutOverAt);
    }
}
