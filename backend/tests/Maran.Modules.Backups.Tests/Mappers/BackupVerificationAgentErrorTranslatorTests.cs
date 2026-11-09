using Maran.Modules.Backups.Mappers;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Backups.Tests.Mappers;

/// <summary>
/// Which of the agent's refusals of a verification say something about the artifact, and which only
/// say the verification could not run.
/// </summary>
public sealed class BackupVerificationAgentErrorTranslatorTests
{
    /// <summary>The three refusals that are findings are named with this module's own codes.</summary>
    /// <param name="agentCode">The code the agent client reported.</param>
    /// <param name="type">The kind it carried.</param>
    /// <param name="finding">The finding the module records.</param>
    [Theory]
    [InlineData("AgentValidationFailed", ErrorType.Validation, "BackupVerificationFailed")]
    [InlineData("AgentNotFound", ErrorType.NotFound, "BackupVerificationArtifactMissing")]
    [InlineData("AgentArchiveUnopenable", ErrorType.Validation, "BackupVerificationArchiveKeyRejected")]
    public void A_refusal_about_the_artifact_is_named_as_a_finding(string agentCode, ErrorType type, string finding)
    {
        Assert.Equal(finding, BackupVerificationAgentErrorTranslator.ToFindingCode(Error.Of(agentCode, type)));
    }

    /// <summary>Every other refusal is not a finding, so it is neither recorded nor mailed as one.</summary>
    /// <param name="agentCode">The code the agent client reported.</param>
    /// <param name="type">The kind it carried.</param>
    /// <remarks>
    /// The inverse control for the test above: a translator that named every refusal a finding passes
    /// that one and fails here. The input-validation refusal shares the validation KIND with the
    /// artifact refusal and is the case that tells code from kind.
    /// </remarks>
    [Theory]
    [InlineData("AgentInvalidInput", ErrorType.Validation)]
    [InlineData("AgentAlreadyExists", ErrorType.Conflict)]
    [InlineData("AgentSystemFailure", ErrorType.Failure)]
    [InlineData("AgentUnavailable", ErrorType.Unavailable)]
    [InlineData("AgentSaturated", ErrorType.Unavailable)]
    [InlineData("AgentBelowFreeSpaceFloor", ErrorType.Unavailable)]
    [InlineData("AgentNotImplemented", ErrorType.Unavailable)]
    public void A_refusal_that_says_nothing_about_the_artifact_is_not_a_finding(string agentCode, ErrorType type)
    {
        Assert.Null(BackupVerificationAgentErrorTranslator.ToFindingCode(Error.Of(agentCode, type)));
    }

    /// <summary>The busy refusal is given this module's own words and keeps its kind.</summary>
    [Fact]
    public void The_busy_refusal_is_named_as_the_accounts_operation_still_running()
    {
        var renamed = BackupVerificationAgentErrorTranslator.ToNotPerformed(
            Error.Of("AgentAlreadyExists", ErrorType.Conflict));

        Assert.Equal("AccountBackupOperationRunning", renamed.Code);
        Assert.Equal(ErrorType.Conflict, renamed.Type);
    }

    /// <summary>Any other refusal that could not be performed is passed through unchanged.</summary>
    [Fact]
    public void Any_other_refusal_that_could_not_be_performed_is_passed_through_unchanged()
    {
        var error = Error.Of("AgentUnavailable", ErrorType.Unavailable);

        Assert.Equal(error, BackupVerificationAgentErrorTranslator.ToNotPerformed(error));
    }
}
