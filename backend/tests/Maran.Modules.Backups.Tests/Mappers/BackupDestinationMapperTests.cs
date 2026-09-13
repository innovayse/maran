using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Mappers;
using Maran.Modules.Backups.Tests.TestSupport;

namespace Maran.Modules.Backups.Tests.Mappers;

/// <summary>The agent-facing restatement of a recorded destination.</summary>
public sealed class BackupDestinationMapperTests
{
    /// <summary>A local destination carries no path, because the agent refuses one that does.</summary>
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

        Assert.Equal(AgentBackupDestinationKind.Local, agentDestination.Kind);
        Assert.Equal(string.Empty, agentDestination.Path);
    }

    /// <summary>A local destination carries no S3 field either, which the agent also refuses.</summary>
    [Fact]
    public void Local_destinations_carry_no_s3_fields()
    {
        var agentDestination = BackupDestinationMapper.ForAgent(BackupsTestContext.DefaultDestinationRow());

        Assert.Equal(string.Empty, agentDestination.S3Bucket);
        Assert.Equal(string.Empty, agentDestination.S3Region);
        Assert.Equal(string.Empty, agentDestination.S3Endpoint);
        Assert.Equal(string.Empty, agentDestination.S3AccessKeyId.Reveal());
        Assert.Equal(string.Empty, agentDestination.S3SecretAccessKey.Reveal());
        Assert.False(agentDestination.S3PathStyle);
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
        var rendered = BackupDestinationMapper.ForAgent(BackupsTestContext.DefaultDestinationRow()).ToString();

        Assert.Contains("S3AccessKeyId = [redacted]", rendered, StringComparison.Ordinal);
        Assert.Contains("S3SecretAccessKey = [redacted]", rendered, StringComparison.Ordinal);
    }
}
