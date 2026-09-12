using Maran.Agent.Client.Services.SftpService;
using Maran.Modules.Sftp.IntegrationEvents.Handlers;
using Maran.Modules.Sftp.Tests.TestSupport;
using Maran.Sdk.Events;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Sftp.Tests.IntegrationEvents.Handlers;

/// <summary>
/// What the Sftp module does when an account is suspended and when it is resumed — the half of the
/// cascade that takes away a live WRITE credential rather than a page a visitor sees.
/// </summary>
public sealed class AccountSuspensionCascadeTests
{
    /// <summary>The system user name the events carry.</summary>
    private const string Username = "acme";

    /// <summary>The account id the events carry.</summary>
    private static readonly Guid Account = Guid.NewGuid();

    /// <summary>Suspending an account locks every SFTP login the host holds for it.</summary>
    /// <remarks>
    /// The defect this handler exists for, measured against a real OpenSSH daemon before it existed:
    /// an SFTP login is its own passwd entry sharing the account's uid, so the
    /// <c>usermod --lock &lt;account&gt;</c> a suspension performs reaches none of them, and the same
    /// login that authenticated before the suspension authenticated after it.
    /// </remarks>
    [Fact]
    public async Task Suspending_an_account_locks_every_sftp_login_the_host_holds_for_it()
    {
        var agent = new RecordingAgentSftpClient();

        await new AccountSuspendingHandler(agent).HandleAsync(
            new AccountSuspending(Account, Username), CancellationToken.None);

        var call = Assert.Single(agent.AccountLocks);
        Assert.Equal(Username, call.AccountUsername);
        Assert.True(call.Locked);
    }

    /// <summary>Resuming an account unlocks them again.</summary>
    /// <remarks>
    /// The inverse control for the assertion above: a handler that locked unconditionally in both
    /// directions would satisfy that one and fail this.
    /// </remarks>
    [Fact]
    public async Task Resuming_an_account_unlocks_every_sftp_login_again()
    {
        var agent = new RecordingAgentSftpClient();

        await new AccountResumingHandler(agent).HandleAsync(
            new AccountResuming(Account, Username), CancellationToken.None);

        var call = Assert.Single(agent.AccountLocks);
        Assert.Equal(Username, call.AccountUsername);
        Assert.False(call.Locked);
    }

    /// <summary>Neither handler deletes a login or resets a password.</summary>
    /// <remarks>
    /// <para>
    /// Suspension revokes a key; it must not destroy what the key opened, and it must give back the
    /// SAME credential rather than a new one the customer would have to be told about. A handler that
    /// deleted the logins on suspend and re-created them on resume would pass both tests above and
    /// would lock every customer of a reactivated account out of their own files.
    /// </para>
    /// <para>
    /// The positive control is that the lock call WAS made in the same run, so this cannot pass for a
    /// handler that does nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Neither_handler_deletes_a_login_or_resets_a_password()
    {
        var agent = new RecordingAgentSftpClient();

        await new AccountSuspendingHandler(agent).HandleAsync(
            new AccountSuspending(Account, Username), CancellationToken.None);
        await new AccountResumingHandler(agent).HandleAsync(
            new AccountResuming(Account, Username), CancellationToken.None);

        Assert.Equal(2, agent.AccountLocks.Count);
        Assert.Empty(agent.Deletes);
        Assert.Empty(agent.PasswordChanges);
    }

    /// <summary>Neither handler names a login, because the host's own list is the authoritative one.</summary>
    /// <remarks>
    /// The account is addressed and no login is, so this module's rows never enter the decision. A
    /// login the panel has forgotten is exactly the one that would keep letting a suspended customer
    /// write to their home, and a handler that looped over <c>SftpUsers</c> would miss precisely
    /// that one while passing every other test in this file.
    /// </remarks>
    [Fact]
    public async Task The_suspension_addresses_the_account_and_never_a_login_from_this_modules_rows()
    {
        var agent = new RecordingAgentSftpClient();

        await new AccountSuspendingHandler(agent).HandleAsync(
            new AccountSuspending(Account, Username), CancellationToken.None);

        Assert.Empty(agent.Creates);
        Assert.Equal([Username], agent.AccountLocks.Select(call => { return call.AccountUsername; }));
    }

    /// <summary>The count the agent answered with is reported onto the cascade for the publisher.</summary>
    /// <remarks>
    /// The Accounts handler renders the operator's attestation and cannot ask the host about a cull —
    /// a cull is an event with no afterwards to observe — so this write is the only path the number
    /// has. A handler that locked correctly and reported nothing would satisfy every other test in
    /// this class.
    /// </remarks>
    [Fact]
    public async Task The_count_the_agent_answered_with_is_reported_onto_the_cascade()
    {
        var agent = new RecordingAgentSftpClient
        {
            SetAccountLoginsLockedResult = Result<AccountLoginLockOutcomeDto>.Ok(
                new AccountLoginLockOutcomeDto(2)),
        };
        var message = new AccountSuspending(Account, Username);

        await new AccountSuspendingHandler(agent).HandleAsync(message, CancellationToken.None);

        Assert.True(message.Report.SessionCullReported);
        Assert.Equal(2u, message.Report.SessionsEnded);
    }

    /// <summary>
    /// A count the host did not give is passed through as an absence, and the report still records
    /// that a subscriber answered. This is the axis that goes blind: substituting zero here would make
    /// the attestation state a completeness claim over an answer the host never gave.
    /// </summary>
    [Fact]
    public async Task A_count_the_host_did_not_give_is_reported_as_an_absence_and_not_as_zero()
    {
        var agent = new RecordingAgentSftpClient
        {
            SetAccountLoginsLockedResult = Result<AccountLoginLockOutcomeDto>.Ok(
                new AccountLoginLockOutcomeDto(null)),
        };
        var message = new AccountSuspending(Account, Username);

        await new AccountSuspendingHandler(agent).HandleAsync(message, CancellationToken.None);

        Assert.True(message.Report.SessionCullReported);
        Assert.Null(message.Report.SessionsEnded);
    }

    /// <summary>
    /// The inverse control, and the one that keeps the publisher honest: a refused lock reports
    /// NOTHING onto the cascade, so an aborted suspension can never leave a report the publisher would
    /// read as a cull that ran.
    /// </summary>
    [Fact]
    public async Task A_refused_lock_reports_nothing_onto_the_cascade()
    {
        var agent = new RecordingAgentSftpClient
        {
            SetAccountLoginsLockedResult = Result<AccountLoginLockOutcomeDto>.Fail(
                Error.Of("AgentUnavailable", ErrorType.Failure)),
        };
        var message = new AccountSuspending(Account, Username);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await new AccountSuspendingHandler(agent).HandleAsync(message, CancellationToken.None);
        });

        Assert.False(message.Report.SessionCullReported);
        Assert.Null(message.Report.SessionsEnded);
    }

    /// <summary>A refusal from the agent aborts the suspension instead of being swallowed.</summary>
    [Fact]
    public async Task A_refused_suspension_aborts_rather_than_being_swallowed()
    {
        var agent = new RecordingAgentSftpClient
        {
            SetAccountLoginsLockedResult = Result<AccountLoginLockOutcomeDto>.Fail(
                Error.Of("AgentUnavailable", ErrorType.Failure)),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await new AccountSuspendingHandler(agent).HandleAsync(
                new AccountSuspending(Account, Username), CancellationToken.None);
        });
    }

    /// <summary>A refusal from the agent aborts the resumption too.</summary>
    [Fact]
    public async Task A_refused_resumption_aborts_rather_than_being_swallowed()
    {
        var agent = new RecordingAgentSftpClient
        {
            SetAccountLoginsLockedResult = Result<AccountLoginLockOutcomeDto>.Fail(
                Error.Of("AgentUnavailable", ErrorType.Failure)),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await new AccountResumingHandler(agent).HandleAsync(
                new AccountResuming(Account, Username), CancellationToken.None);
        });
    }
}
