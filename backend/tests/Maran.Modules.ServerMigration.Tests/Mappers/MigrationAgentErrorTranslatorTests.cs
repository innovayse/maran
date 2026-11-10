using Maran.Agent.Client.Services.TransferService;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Mappers;
using Maran.SharedKernel.Results;

namespace Maran.Modules.ServerMigration.Tests.Mappers;

/// <summary>
/// The one place a failed call to a remote server becomes this module's error: the reason decides the code, a failure
/// that describes the server is recorded and one that describes the agent is not, and the agent's words never travel.
/// </summary>
public sealed class MigrationAgentErrorTranslatorTests
{
    /// <summary>Each reason the agent states has its own code and kind.</summary>
    /// <param name="reason">Why the source refused.</param>
    /// <param name="code">The code answered.</param>
    /// <param name="type">The kind answered.</param>
    [Theory]
    [InlineData(SourceFailureReason.HostKeyChanged, "RemoteServerHostKeyChanged", ErrorType.Conflict)]
    [InlineData(SourceFailureReason.AuthRefused, "RemoteServerAuthRefused", ErrorType.Conflict)]
    [InlineData(SourceFailureReason.Unreachable, "RemoteServerUnreachable", ErrorType.Unavailable)]
    [InlineData(SourceFailureReason.AddressRefused, "RemoteServerAddressRefused", ErrorType.Validation)]
    public void Each_reason_has_its_own_code(SourceFailureReason reason, string code, ErrorType type)
    {
        var error = MigrationAgentErrorTranslator.Translate(Error.Of("AgentSystemFailure", ErrorType.Failure), reason);

        Assert.Equal(code, error.Code);
        Assert.Equal(type, error.Type);
    }

    /// <summary>A failure with no reason is the agent's outage when the agent could not be reached, and a failed operation otherwise.</summary>
    [Fact]
    public void A_failure_with_no_reason_is_the_agents_outage_or_a_failed_operation()
    {
        var outage = MigrationAgentErrorTranslator.Translate(
            Error.Of("AgentUnavailable", ErrorType.Unavailable), SourceFailureReason.Unspecified);
        var broken = MigrationAgentErrorTranslator.Translate(
            Error.Of("AgentSystemFailure", ErrorType.Failure), SourceFailureReason.Unspecified);

        Assert.Equal("MigrationAgentUnavailable", outage.Code);
        Assert.Equal(ErrorType.Unavailable, outage.Type);
        Assert.Equal("MigrationDiscoveryFailed", broken.Code);
        Assert.Equal(ErrorType.Failure, broken.Type);
    }

    /// <summary>The agent's own code never becomes the answer, whatever it was.</summary>
    [Fact]
    public void The_agents_own_code_never_becomes_the_answer()
    {
        var error = MigrationAgentErrorTranslator.Translate(
            Error.Of("AgentSecretsLookLikeThis", ErrorType.Failure), SourceFailureReason.Unspecified);

        Assert.DoesNotContain("Secrets", error.Code, StringComparison.Ordinal);
    }

    /// <summary>A reason that describes the server is recorded as a failure kind; no reason records nothing.</summary>
    [Fact]
    public void A_reason_that_describes_the_server_is_recorded()
    {
        Assert.Equal(RemoteFailureKind.HostKeyChanged, MigrationAgentErrorTranslator.RecordedFailureOf(SourceFailureReason.HostKeyChanged));
        Assert.Equal(RemoteFailureKind.AuthRefused, MigrationAgentErrorTranslator.RecordedFailureOf(SourceFailureReason.AuthRefused));
        Assert.Equal(RemoteFailureKind.Unreachable, MigrationAgentErrorTranslator.RecordedFailureOf(SourceFailureReason.Unreachable));
        Assert.Equal(RemoteFailureKind.AddressRefused, MigrationAgentErrorTranslator.RecordedFailureOf(SourceFailureReason.AddressRefused));
        Assert.Null(MigrationAgentErrorTranslator.RecordedFailureOf(SourceFailureReason.Unspecified));
    }

    /// <summary>Every reason the agent client defines has an answer, so a reason added later is not silently treated as none.</summary>
    [Fact]
    public void Every_reason_the_client_defines_is_covered()
    {
        foreach (var reason in Enum.GetValues<SourceFailureReason>().Where(value =>
        {
            return value != SourceFailureReason.Unspecified;
        }))
        {
            Assert.NotNull(MigrationAgentErrorTranslator.RecordedFailureOf(reason));
            Assert.StartsWith("RemoteServer", MigrationAgentErrorTranslator.Translate(
                Error.Of("AgentSystemFailure", ErrorType.Failure), reason).Code, StringComparison.Ordinal);
        }
    }
}
