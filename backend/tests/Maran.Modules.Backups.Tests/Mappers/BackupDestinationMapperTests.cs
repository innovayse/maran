using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Mappers;
using Maran.Modules.Backups.Tests.TestSupport;

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
}
