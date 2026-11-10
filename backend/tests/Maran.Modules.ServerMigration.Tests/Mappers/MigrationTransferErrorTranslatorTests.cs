using Maran.Agent.Client.Services.TransferService;
using Maran.Modules.ServerMigration.Mappers;
using Maran.SharedKernel.Results;

namespace Maran.Modules.ServerMigration.Tests.Mappers;

/// <summary>Which code a failed pull or database transfer is recorded as (issue #197, phase 2).</summary>
public sealed class MigrationTransferErrorTranslatorTests
{
    /// <summary>An error the agent client produced.</summary>
    private static readonly Error AgentError = Error.Of("AgentSystemFailure", ErrorType.Failure);

    /// <summary>Each failure of a transfer has its own code and kind.</summary>
    [Theory]
    [InlineData(TransferFailureReason.DiskInsufficient, "MigrationDiskInsufficient", ErrorType.Conflict)]
    [InlineData(TransferFailureReason.PullFailed, "MigrationPullFailed", ErrorType.Failure)]
    [InlineData(TransferFailureReason.PullIncomplete, "MigrationPullIncomplete", ErrorType.Failure)]
    [InlineData(TransferFailureReason.DumpFailed, "MigrationDumpFailed", ErrorType.Failure)]
    [InlineData(TransferFailureReason.DumpTooLarge, "MigrationDumpTooLarge", ErrorType.Conflict)]
    [InlineData(TransferFailureReason.LoadRefused, "MigrationDumpRefused", ErrorType.Conflict)]
    [InlineData(TransferFailureReason.LoadFailed, "MigrationDumpLoadFailed", ErrorType.Failure)]
    public void A_transfer_reason_maps_to_its_own_code(TransferFailureReason reason, string code, ErrorType kind)
    {
        var translated = MigrationTransferErrorTranslator.Translate(AgentError, SourceFailureReason.Unspecified, reason);

        Assert.Equal(code, translated.Code);
        Assert.Equal(kind, translated.Type);
    }

    /// <summary>A transfer reason outranks the connection reason: the dump was refused after the connection worked.</summary>
    [Fact]
    public void A_transfer_reason_is_read_before_a_connection_reason()
    {
        var translated = MigrationTransferErrorTranslator.Translate(
            AgentError, SourceFailureReason.Unreachable, TransferFailureReason.LoadRefused);

        Assert.Equal("MigrationDumpRefused", translated.Code);
    }

    /// <summary>With no transfer reason a connection failure keeps its phase 1 code, and anything else is a fault.</summary>
    [Fact]
    public void Without_a_transfer_reason_the_connection_translation_or_a_fault_applies()
    {
        Assert.Equal(
            "RemoteServerHostKeyChanged",
            MigrationTransferErrorTranslator.Translate(AgentError, SourceFailureReason.HostKeyChanged, TransferFailureReason.Unspecified).Code);
        Assert.Equal(
            "MigrationTransferFaulted",
            MigrationTransferErrorTranslator.Translate(AgentError, SourceFailureReason.Unspecified, TransferFailureReason.Unspecified).Code);
    }
}
