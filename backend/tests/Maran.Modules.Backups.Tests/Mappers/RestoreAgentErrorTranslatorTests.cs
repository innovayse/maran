using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Mappers;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Backups.Tests.Mappers;

/// <summary>
/// Covers what a restore stream's terminal event is named, and in particular the one naming that an
/// operator reads during a recovery.
/// </summary>
public sealed class RestoreAgentErrorTranslatorTests
{
    /// <summary>
    /// An agent refusal of the validation KIND is renamed to this module's artifact code, because on
    /// a restore stream it can only be about the ARTIFACT.
    /// </summary>
    /// <remarks>
    /// The pinned defect, measured in a browser against a deliberately corrupted archive: the dialog
    /// showed the shared "correct them and try again" sentence directly above the panel's own note
    /// saying that re-entering the confirmation could not change the answer. Two adjacent paragraphs
    /// contradicting each other, and the operator reads the wrong one first. The values are asserted
    /// rather than bounded — "some error came back" was true before this fix too.
    /// </remarks>
    [Theory]
    [InlineData("AgentValidationFailed")]
    [InlineData("AgentInvalidInput")]
    public void An_agent_refusal_of_the_validation_kind_is_named_as_a_refused_artifact(string agentCode)
    {
        var terminal = new BackupRestoreEvent(
            BackupRestoreEventKind.Failed,
            0,
            string.Empty,
            null,
            Error.Of(agentCode, ErrorType.Validation));

        var failure = RestoreAgentErrorTranslator.ToFailure(terminal);

        Assert.Equal("RestoreArtifactRejected", failure.Code);
    }

    /// <summary>The renaming keeps the KIND, so a refused artifact still answers HTTP 400.</summary>
    /// <remarks>
    /// Stated separately from the code, because the two have been separated before: carrying only
    /// the code is exactly what made a corrupt artifact answer HTTP 500 earlier today, and a fix
    /// that renamed the code while dropping the kind would reintroduce that defect while passing the
    /// test above.
    /// </remarks>
    [Fact]
    public void A_refused_artifact_keeps_the_validation_kind_it_arrived_with()
    {
        var terminal = new BackupRestoreEvent(
            BackupRestoreEventKind.Failed,
            0,
            string.Empty,
            null,
            Error.Of("AgentValidationFailed", ErrorType.Validation));

        var failure = RestoreAgentErrorTranslator.ToFailure(terminal);

        Assert.Equal(ErrorType.Validation, failure.Type);
    }

    /// <summary>
    /// An agent failure that is NOT of the validation kind is passed through under the agent's own
    /// code, untouched.
    /// </summary>
    /// <remarks>
    /// The other branch, and the one that stops the renaming from swallowing everything. A rewrite
    /// that named every agent refusal a refused artifact would pass every assertion above and would
    /// tell an operator whose server ran out of disk that their backup copy was corrupt.
    /// </remarks>
    [Fact]
    public void An_agent_failure_of_another_kind_keeps_the_agents_own_code()
    {
        var terminal = new BackupRestoreEvent(
            BackupRestoreEventKind.Failed,
            0,
            string.Empty,
            null,
            Error.Of("AgentSystemFailure", ErrorType.Failure));

        var failure = RestoreAgentErrorTranslator.ToFailure(terminal);

        Assert.Equal("AgentSystemFailure", failure.Code);
        Assert.Equal(ErrorType.Failure, failure.Type);
    }

    /// <summary>An ending the agent stated no error for keeps the code this module names it by.</summary>
    [Theory]
    [InlineData(BackupRestoreEventKind.Dropped, "RestoreStreamDropped")]
    [InlineData(BackupRestoreEventKind.Idle, "RestoreStreamIdle")]
    [InlineData(BackupRestoreEventKind.Truncated, "RestoreTruncated")]
    [InlineData(BackupRestoreEventKind.Cancelled, "RestoreCancelled")]
    public void An_ending_that_carries_no_error_keeps_this_modules_own_code(
        BackupRestoreEventKind kind,
        string expectedCode)
    {
        var terminal = new BackupRestoreEvent(kind, 0, string.Empty, null, null);

        var failure = RestoreAgentErrorTranslator.ToFailure(terminal);

        Assert.Equal(expectedCode, failure.Code);
        Assert.Equal(ErrorType.Failure, failure.Type);
    }

    /// <summary>
    /// The agent's busy refusal is renamed to this module's account-busy code, because on a restore
    /// stream the wire's already-exists can only be the per-account lock.
    /// </summary>
    /// <remarks>
    /// The agent answers a second operation on one account with <c>BackupError::AlreadyRunning</c>
    /// and puts it on the wire as already-exists, which the shared table renders as "this already
    /// exists on your server, so nothing was created again". On a restore that sentence is false in
    /// both halves and, worse, it asserts a successful idempotent convergence — so the customer
    /// reads "already done" for an operation that did not run and never retries. The separation is
    /// sound because <c>BackupError::AlreadyExists</c> has exactly one production site in the agent,
    /// inside the creation path.
    /// </remarks>
    [Fact]
    public void An_agent_already_exists_on_a_restore_is_named_as_the_accounts_operation_still_running()
    {
        var terminal = new BackupRestoreEvent(
            BackupRestoreEventKind.Failed,
            0,
            string.Empty,
            null,
            Error.Of("AgentAlreadyExists", ErrorType.Conflict));

        var failure = RestoreAgentErrorTranslator.ToFailure(terminal);

        Assert.Equal("AccountBackupOperationRunning", failure.Code);
        Assert.Equal(ErrorType.Conflict, failure.Type);
    }

    /// <summary>Every other agent failure the stream carries is passed through untouched.</summary>
    /// <remarks>
    /// The inverse control the renaming owes. A translator mutated to answer the busy code for
    /// everything passes the test above and loses every distinction the stream carries — most of
    /// all <c>RolledBack</c>, which is the most serious answer this area produces.
    /// </remarks>
    [Fact]
    public void An_agent_failure_that_is_not_the_lock_keeps_the_code_it_arrived_with()
    {
        var terminal = new BackupRestoreEvent(
            BackupRestoreEventKind.Failed,
            0,
            string.Empty,
            null,
            Error.Of("AgentSystemFailure", ErrorType.Failure));

        var failure = RestoreAgentErrorTranslator.ToFailure(terminal);

        Assert.Equal("AgentSystemFailure", failure.Code);
        Assert.Equal(ErrorType.Failure, failure.Type);
    }
}
