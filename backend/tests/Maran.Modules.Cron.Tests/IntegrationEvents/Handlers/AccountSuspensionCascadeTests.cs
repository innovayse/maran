using Maran.Modules.Cron.IntegrationEvents.Handlers;
using Maran.Modules.Cron.Tests.TestSupport;
using Maran.Sdk.Events;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Cron.Tests.IntegrationEvents.Handlers;

/// <summary>
/// What the Cron module does when an account is suspended and when it is resumed. The pair is in one
/// file because the whole point of the design is the relationship between them: the suspension
/// writes a marker that is NOT the customer's per-entry switch, so the resume can put every entry
/// back exactly as its owner left it.
/// </summary>
public sealed class AccountSuspensionCascadeTests
{
    /// <summary>The system user name the events carry.</summary>
    private const string Username = "acme";

    /// <summary>The account id the events carry.</summary>
    private static readonly Guid Account = Guid.NewGuid();

    /// <summary>Suspending an account suppresses its whole crontab in one call.</summary>
    /// <remarks>
    /// The defect this handler exists for: before it, a suspended customer's scheduled jobs went on
    /// firing on schedule while the panel told billing the account was stopped.
    /// </remarks>
    [Fact]
    public async Task Suspending_an_account_suppresses_its_whole_crontab()
    {
        var agent = new RecordingAgentCronClient();

        await new AccountSuspendingHandler(agent).HandleAsync(
            new AccountSuspending(Account, Username), CancellationToken.None);

        var call = Assert.Single(agent.AccountSuspensions);
        Assert.Equal(Username, call.AccountUsername);
        Assert.True(call.Suspended);
    }

    /// <summary>Resuming an account lets its whole crontab run again.</summary>
    /// <remarks>
    /// The inverse control for the assertion above: a handler that suspended unconditionally in both
    /// directions would satisfy that one and fail this.
    /// </remarks>
    [Fact]
    public async Task Resuming_an_account_lets_its_whole_crontab_run_again()
    {
        var agent = new RecordingAgentCronClient();

        await new AccountResumingHandler(agent).HandleAsync(
            new AccountResuming(Account, Username), CancellationToken.None);

        var call = Assert.Single(agent.AccountSuspensions);
        Assert.Equal(Username, call.AccountUsername);
        Assert.False(call.Suspended);
    }

    /// <summary>Neither handler ever writes the customer's own per-entry enablement.</summary>
    /// <remarks>
    /// <para>
    /// <b>This is the law of the whole piece, and it is the only test that can catch its breach.</b>
    /// The obvious implementation of "suspend an account's cron" is a loop over the per-entry
    /// enablement, and it would pass both tests above: every entry would indeed stop firing. What it
    /// would destroy is invisible until the resume — the record of which entries the customer had
    /// switched off themselves — and this module keeps no rows, so there is no second copy of that
    /// choice anywhere to notice the loss against.
    /// </para>
    /// <para>
    /// So the assertion is about the call that must NOT be made. Its positive control is that the
    /// account-wide call WAS made in the same run, which is what stops it passing for a handler that
    /// does nothing at all.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Neither_handler_touches_the_per_entry_switch_that_belongs_to_the_customer()
    {
        var agent = new RecordingAgentCronClient();

        await new AccountSuspendingHandler(agent).HandleAsync(
            new AccountSuspending(Account, Username), CancellationToken.None);
        await new AccountResumingHandler(agent).HandleAsync(
            new AccountResuming(Account, Username), CancellationToken.None);

        Assert.Equal(2, agent.AccountSuspensions.Count);
        Assert.Empty(agent.EnabledChanges);
    }

    /// <summary>A refusal from the agent aborts the suspension instead of being swallowed.</summary>
    /// <remarks>
    /// Throwing is a subscriber's only way to abort. Returning quietly would leave the account marked
    /// suspended with its jobs still firing, which is the state this handler was written to end.
    /// </remarks>
    [Fact]
    public async Task A_refused_suspension_aborts_rather_than_being_swallowed()
    {
        var agent = new RecordingAgentCronClient
        {
            SetAccountSuspendedResult = Result<bool>.Fail(Error.Of("AgentUnavailable", ErrorType.Failure)),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await new AccountSuspendingHandler(agent).HandleAsync(
                new AccountSuspending(Account, Username), CancellationToken.None);
        });
    }

    /// <summary>A refusal from the agent aborts the resumption too.</summary>
    /// <remarks>
    /// The mirror, and not a copy: an account marked active whose scheduled jobs never restarted is
    /// the panel telling a paying customer their service is back when part of it is not.
    /// </remarks>
    [Fact]
    public async Task A_refused_resumption_aborts_rather_than_being_swallowed()
    {
        var agent = new RecordingAgentCronClient
        {
            SetAccountSuspendedResult = Result<bool>.Fail(Error.Of("AgentUnavailable", ErrorType.Failure)),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await new AccountResumingHandler(agent).HandleAsync(
                new AccountResuming(Account, Username), CancellationToken.None);
        });
    }
}
