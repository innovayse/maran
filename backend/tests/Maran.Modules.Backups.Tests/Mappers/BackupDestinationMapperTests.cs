using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Mappers;
using Maran.Modules.Backups.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Backups.Tests.Mappers;

/// <summary>The agent-facing restatement of a recorded destination.</summary>
public sealed class BackupDestinationMapperTests
{
    /// <summary>A local destination is the local arm, which has no path, because the agent refuses one that does.</summary>
    /// <remarks>
    /// <b>This is the test the defect it guards had none of.</b> The mapper used to put the
    /// configured local root in <c>Path</c>, and the agent's <c>validated_local</c> answers
    /// <c>InvalidInput</c> — "a local destination carries no path: the agent's backup root is its
    /// own" — for any local destination whose path is not empty. Every create, restore, delete and
    /// retention call the panel made would have been refused by a real agent, and no panel-side test
    /// could see it because the client is stubbed everywhere.
    /// </remarks>
    [Fact]
    public void Local_destinations_carry_no_path()
    {
        var agentDestination = BackupDestinationMapper.ForAgent(BackupsTestContext.DefaultDestinationRow());

        Assert.IsType<AgentLocalBackupDestination>(agentDestination);
    }

    /// <summary>An object-store row is restated as the S3 arm, carrying no credential this build stores.</summary>
    /// <remarks>
    /// The inverse control for the local test above: a mapper that returned the local arm for every
    /// row would pass that test and fail this one.
    /// </remarks>
    [Fact]
    public void Object_store_destinations_are_the_s3_arm_with_empty_members()
    {
        var row = new BackupDestination(
            Guid.NewGuid(),
            "Somebody's bucket",
            BackupDestinationKind.S3,
            string.Empty,
            isDefault: false,
            BackupsTestContext.SeedInstant);

        var agentDestination = Assert.IsType<AgentS3BackupDestination>(BackupDestinationMapper.ForAgent(row));

        Assert.Equal(string.Empty, agentDestination.Path);
        Assert.Equal(string.Empty, agentDestination.Bucket);
        Assert.Equal(string.Empty, agentDestination.Region);
        Assert.Equal(string.Empty, agentDestination.Endpoint);
        Assert.Equal(string.Empty, agentDestination.AccessKeyId.Reveal());
        Assert.Equal(string.Empty, agentDestination.SecretAccessKey.Reveal());
        Assert.False(agentDestination.PathStyle);
    }

    /// <summary>Formatting a destination never prints anything a credential could hide behind.</summary>
    /// <remarks>
    /// The habit has to exist before there is a secret: the two credential members are
    /// <c>SensitiveString</c> so that the record's generated <c>ToString</c> prints the mask, and
    /// this build stores no credential to leak only because it stores none yet.
    /// </remarks>
    [Fact]
    public void Formatting_a_destination_prints_the_mask_where_a_credential_would_be()
    {
        var row = new BackupDestination(
            Guid.NewGuid(),
            "Somebody's bucket",
            BackupDestinationKind.S3,
            string.Empty,
            isDefault: false,
            BackupsTestContext.SeedInstant);

        var rendered = BackupDestinationMapper.ForAgent(row).ToString();

        Assert.Contains("AccessKeyId = [redacted]", rendered, StringComparison.Ordinal);
        Assert.Contains("SecretAccessKey = [redacted]", rendered, StringComparison.Ordinal);
    }

    /// <summary>The key of a destination, for the tests that need one.</summary>
    private static readonly string Key = new('c', 64);

    /// <summary>A local destination that has been given a key, encrypting or not.</summary>
    /// <param name="encrypting">Whether new backups are sealed.</param>
    /// <returns>The destination.</returns>
    private static BackupDestination LocalWithKey(bool encrypting)
    {
        var row = BackupsTestContext.DefaultDestinationRow();
        row.EnableArchiveEncryption(Key);
        if (!encrypting)
        {
            row.DisableArchiveEncryption();
        }

        return row;
    }

    /// <summary>A destination that never had encryption carries no key on any call.</summary>
    [Fact]
    public void A_destination_without_a_key_carries_none_for_reading_or_for_writing()
    {
        var row = BackupsTestContext.DefaultDestinationRow();

        Assert.Null(Assert.IsType<AgentLocalBackupDestination>(BackupDestinationMapper.ForAgent(row)).ArchiveKey);
        Assert.Null(Assert.IsType<AgentLocalBackupDestination>(BackupDestinationMapper.ForNewBackup(row)).ArchiveKey);
    }

    /// <summary>While encryption is on, both a new backup and a read carry the key.</summary>
    [Fact]
    public void An_encrypting_destination_carries_its_key_for_reading_and_for_writing()
    {
        var row = LocalWithKey(encrypting: true);

        var reading = Assert.IsType<AgentLocalBackupDestination>(BackupDestinationMapper.ForAgent(row));
        var writing = Assert.IsType<AgentLocalBackupDestination>(BackupDestinationMapper.ForNewBackup(row));

        Assert.Equal(Key, reading.ArchiveKey!.Reveal());
        Assert.Equal(Key, writing.ArchiveKey!.Reveal());
    }

    /// <summary>
    /// With encryption switched off the key is still sent for reading, and NOT for writing: on a
    /// creation the key's presence is the instruction to seal.
    /// </summary>
    [Fact]
    public void A_destination_with_encryption_off_still_reads_with_its_key_and_writes_without_it()
    {
        var row = LocalWithKey(encrypting: false);

        var reading = Assert.IsType<AgentLocalBackupDestination>(BackupDestinationMapper.ForAgent(row));
        var writing = Assert.IsType<AgentLocalBackupDestination>(BackupDestinationMapper.ForNewBackup(row));

        Assert.Equal(Key, reading.ArchiveKey!.Reveal());
        Assert.Null(writing.ArchiveKey);
    }

    /// <summary>Formatting a destination that holds a key prints the mask and never the key.</summary>
    [Fact]
    public void Formatting_a_local_destination_never_prints_the_archive_key()
    {
        var rendered = BackupDestinationMapper.ForAgent(LocalWithKey(encrypting: true)).ToString();

        Assert.Contains("ArchiveKey = [redacted]", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, rendered, StringComparison.Ordinal);
    }

    /// <summary>The resolver hands each kind of call the destination meant for it.</summary>
    [Fact]
    public async Task The_resolver_gives_reads_the_key_and_new_backups_only_while_encryption_is_on()
    {
        await using var dbContext = BackupsTestContext.CreateSeeded(FakeCurrentUser.Admin());
        var row = await dbContext.BackupDestinations.SingleAsync();
        row.EnableArchiveEncryption(Key);
        row.DisableArchiveEncryption();
        await dbContext.SaveChangesAsync();
        var resolver = new Maran.Modules.Backups.Services.BackupDestinationResolver(dbContext);

        var off = (await resolver.ResolveAsync(null, CancellationToken.None)).Value!;
        row.EnableArchiveEncryption(Key);
        await dbContext.SaveChangesAsync();
        var on = (await resolver.ResolveAsync(null, CancellationToken.None)).Value!;

        Assert.Equal(Key, ((AgentLocalBackupDestination)off.Agent).ArchiveKey!.Reveal());
        Assert.Null(((AgentLocalBackupDestination)off.AgentForNewBackup).ArchiveKey);
        Assert.Equal(Key, ((AgentLocalBackupDestination)on.Agent).ArchiveKey!.Reveal());
        Assert.Equal(Key, ((AgentLocalBackupDestination)on.AgentForNewBackup).ArchiveKey!.Reveal());
    }
}
