using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.AccountsService;
using Maran.Modules.Accounts.Commands.ReactivateAccount;
using Maran.Modules.Accounts.Commands.SuspendAccount;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Events;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Accounts.Tests.Commands.SuspendAccount;

/// <summary>
/// Suspension and its reversal, exercised together: the pair is one behaviour — an account can be
/// turned off and back on without losing anything — and testing either alone would leave the state
/// machine half-covered.
/// </summary>
public sealed class AccountSuspensionTests : IDisposable
{
    /// <summary>The instant seeded accounts are created at.</summary>
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The caller address every command in this file carries.</summary>
    private const string Ip = "203.0.113.7";

    /// <summary>The user agent every command in this file carries.</summary>
    private const string Client = "unit-tests";

    /// <summary>The Accounts context under test.</summary>
    private readonly AccountsDbContext _context = CreateDbContext();

    /// <summary>The agent double both handlers drive.</summary>
    private readonly RecordingAgentAccountsClient _agent = new();

    /// <summary>What the handlers recorded as tasks; asserted on by the task-shaped tests below.</summary>
    private readonly RecordingTaskRecorder _tasks = new();

    /// <summary>Builds a journal writing into a writer nothing asserts on; the audit tests do that.</summary>
    /// <returns>The journal.</returns>
    private static AccountAuditJournal Journal()
    {
        return new AccountAuditJournal(new RecordingAuditWriter(), FakeCurrentUser.Admin());
    }

    /// <summary>Builds a fresh, isolated in-memory context.</summary>
    /// <returns>The context.</returns>
    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options);
    }

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>Suspending an active account moves it into suspension.</summary>
    [Fact]
    public async Task Suspending_an_active_account_moves_it_into_suspension()
    {
        var account = await SeedAsync();

        var result = await SuspendAsync(_agent, new StubMessageBus(), account.Id);

        Assert.Equal(AccountStatus.Suspended, result.Value.Status);
        Assert.Equal(AccountStatus.Suspended, (await _context.Accounts.SingleAsync()).Status);
    }

    /// <summary>Suspending an already suspended account is a no op rather than a failure.</summary>
    [Fact]
    public async Task Suspending_an_already_suspended_account_is_a_no_op_rather_than_a_failure()
    {
        // A billing system calls this on every overdue invoice; an error on the second call would
        // make the caller track state the panel already holds.
        var account = await SeedAsync();
        await SuspendAsync(_agent, new StubMessageBus(), account.Id);

        var result = await SuspendAsync(_agent, new StubMessageBus(), account.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountStatus.Suspended, result.Value.Status);
    }

    /// <summary>Reactivating a suspended account puts it back.</summary>
    [Fact]
    public async Task Reactivating_a_suspended_account_puts_it_back()
    {
        var account = await SeedAsync();
        await SuspendAsync(_agent, new StubMessageBus(), account.Id);

        var result = await ReactivateAsync(_agent, new StubMessageBus(), account.Id);

        Assert.Equal(AccountStatus.Active, result.Value.Status);
    }

    /// <summary>Suspending keeps everything else about the account.</summary>
    [Fact]
    public async Task Suspending_keeps_everything_else_about_the_account()
    {
        var account = await SeedAsync();

        await SuspendAsync(_agent, new StubMessageBus(), account.Id);

        var stored = await _context.Accounts.SingleAsync();
        Assert.Equal("acme", stored.Name);
        Assert.Equal("acme.example.com", stored.PrimaryDomain);
        Assert.Equal(Now, stored.CreatedAt);
    }

    /// <summary>Suspending an account that does not exist answers not found.</summary>
    [Fact]
    public async Task Suspending_an_account_that_does_not_exist_answers_not_found()
    {
        var result = await SuspendAsync(_agent, new StubMessageBus(), Guid.NewGuid());

        Assert.Equal("AccountNotFound", result.Error!.Code);
    }

    /// <summary>Reactivating an account that does not exist answers not found.</summary>
    [Fact]
    public async Task Reactivating_an_account_that_does_not_exist_answers_not_found()
    {
        var result = await ReactivateAsync(_agent, new StubMessageBus(), Guid.NewGuid());

        Assert.Equal("AccountNotFound", result.Error!.Code);
    }

    /// <summary>Suspending asks every module to stop before it touches the host.</summary>
    /// <remarks>
    /// The order is the whole of the safety, exactly as it is for deletion: the cascade is invoked
    /// while the account is still active, so a module that refuses leaves nothing half-done.
    /// </remarks>
    [Fact]
    public async Task Suspending_asks_every_module_to_stop_before_it_touches_the_host()
    {
        var account = await SeedAsync();
        var bus = new StubMessageBus();

        await SuspendAsync(_agent, bus, account.Id);

        var announced = Assert.IsType<AccountSuspending>(Assert.Single(bus.Invoked));
        Assert.Equal(account.Id, announced.AccountId);
        Assert.Equal("acme", announced.Username);
        Assert.Equal(["suspend:acme", "observe:acme"], _agent.Calls);
    }

    /// <summary>A module that refuses to stop leaves the account active and the host untouched.</summary>
    [Fact]
    public async Task A_module_that_refuses_to_stop_leaves_the_account_active_and_the_host_untouched()
    {
        var account = await SeedAsync();
        var refusing = new StubMessageBus(new InvalidOperationException("the vhost could not be stubbed"));

        var result = await SuspendAsync(_agent, refusing, account.Id);

        Assert.Equal("AccountSuspensionFailed", result.Error!.Code);
        Assert.Empty(_agent.Calls);
        Assert.Equal(AccountStatus.Active, (await _context.Accounts.SingleAsync()).Status);
    }

    /// <summary>An agent that refuses leaves the row untouched.</summary>
    [Fact]
    public async Task An_agent_that_refuses_leaves_the_row_untouched()
    {
        // The order is the whole subject: the row records what the agent did, so a refusal
        // must not leave the panel claiming an account is suspended while its sites serve.
        var account = await SeedAsync();
        var refusing = new RecordingAgentAccountsClient(Error.Of("AgentSystemFailure", ErrorType.Failure));

        var result = await SuspendAsync(refusing, new StubMessageBus(), account.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Equal(AccountStatus.Active, (await _context.Accounts.SingleAsync()).Status);
    }

    /// <summary>An account that does not exist never reaches the agent.</summary>
    [Fact]
    public async Task An_account_that_does_not_exist_never_reaches_the_agent()
    {
        await SuspendAsync(_agent, new StubMessageBus(), Guid.NewGuid());

        Assert.Empty(_agent.Calls);
    }

    /// <summary>A suspension is refused while the host still serves one of the account's own sites.</summary>
    /// <remarks>
    /// The defect this whole change exists for. Nothing threw: the cascade ran, the agent locked the
    /// login and answered success, and the panel wrote Suspended over a website that was still
    /// answering requests. Completion is now an OBSERVATION, so a vhost that is not the stub refuses
    /// the suspension and leaves the account active.
    /// </remarks>
    [Fact]
    public async Task A_suspension_is_refused_while_the_host_still_serves_one_of_the_accounts_sites()
    {
        var account = await SeedAsync();
        _agent.SuspensionState = new AccountSuspensionStateDto(
            false,
            AccountLoginPasswordState.Usable,
            true,
            [new SiteSuspensionFactDto("stubbed.example.com", true),
             new SiteSuspensionFactDto("serving.example.com", false)],
            0,
            0,
            0,
            []);

        var result = await SuspendAsync(_agent, new StubMessageBus(), account.Id);

        Assert.Equal("AccountSuspensionNotObserved", result.Error!.Code);
        Assert.Equal(AccountStatus.Active, (await _context.Accounts.SingleAsync()).Status);
    }

    /// <summary>A suspension is refused when the agent could not read the vhost directory at all.</summary>
    /// <remarks>
    /// The vacuity guard, on the axis that can go blind. An unreadable directory and an account with
    /// no vhost produce the same empty list, and the empty list is the one that reads as "everything
    /// is suspended" — so a blind answer must never be accepted as a suspended one. Its inverse
    /// control is <see cref="Suspending_an_active_account_moves_it_into_suspension"/>, where the same
    /// empty list over a READABLE directory is accepted.
    /// </remarks>
    [Fact]
    public async Task A_suspension_is_refused_when_the_agent_could_not_read_the_vhost_directory_at_all()
    {
        var account = await SeedAsync();
        _agent.SuspensionState = new AccountSuspensionStateDto(
            true, AccountLoginPasswordState.Locked, false, [], 0, 0, 0, []);

        var result = await SuspendAsync(_agent, new StubMessageBus(), account.Id);

        Assert.Equal("AccountSuspensionNotObserved", result.Error!.Code);
        Assert.Equal(AccountStatus.Active, (await _context.Accounts.SingleAsync()).Status);
    }

    /// <summary>A suspension is refused while the host still shows the account's login unlocked.</summary>
    [Fact]
    public async Task A_suspension_is_refused_while_the_host_still_shows_the_accounts_login_unlocked()
    {
        var account = await SeedAsync();
        var agent = new UnlockedAgentAccountsClient();

        var result = await SuspendAsync(agent, new StubMessageBus(), account.Id);

        Assert.Equal("AccountSuspensionNotObserved", result.Error!.Code);
        Assert.Equal(AccountStatus.Active, (await _context.Accounts.SingleAsync()).Status);
    }

    /// <summary>A reactivation is refused while one of the account's vhosts is still the stub.</summary>
    /// <remarks>
    /// The mirror refusal, and the one that stops "reactivated" meaning "the call returned". Note
    /// what it does NOT do: the site set is the host's, so this refuses on a stub the panel does not
    /// know about too, which is the safe direction — it never certifies a restart it cannot see.
    /// </remarks>
    [Fact]
    public async Task A_reactivation_is_refused_while_one_of_the_accounts_vhosts_is_still_the_stub()
    {
        var account = await SeedAsync();
        await SuspendAsync(_agent, new StubMessageBus(), account.Id);
        _agent.SuspensionState = _agent.SuspensionState with
        {
            Sites = [new SiteSuspensionFactDto("still-stubbed.example.com", true)],
        };

        var result = await ReactivateAsync(_agent, new StubMessageBus(), account.Id);

        Assert.Equal("AccountResumptionNotObserved", result.Error!.Code);
        Assert.Equal(AccountStatus.Suspended, (await _context.Accounts.SingleAsync()).Status);
    }

    /// <summary>A module that refuses to restore leaves the account suspended.</summary>
    [Fact]
    public async Task A_module_that_refuses_to_restore_leaves_the_account_suspended()
    {
        var account = await SeedAsync();
        await SuspendAsync(_agent, new StubMessageBus(), account.Id);
        var refusing = new StubMessageBus(new InvalidOperationException("the vhost could not be restored"));

        var result = await ReactivateAsync(_agent, refusing, account.Id);

        Assert.Equal("AccountResumptionFailed", result.Error!.Code);
        Assert.Equal(AccountStatus.Suspended, (await _context.Accounts.SingleAsync()).Status);
    }

    /// <summary>Reactivating announces the resumption before it touches the host.</summary>
    [Fact]
    public async Task Reactivating_announces_the_resumption_before_it_touches_the_host()
    {
        var account = await SeedAsync();
        await SuspendAsync(_agent, new StubMessageBus(), account.Id);
        _agent.Calls.Clear();
        var bus = new StubMessageBus();

        await ReactivateAsync(_agent, bus, account.Id);

        var announced = Assert.IsType<AccountResuming>(Assert.Single(bus.Invoked));
        Assert.Equal(account.Id, announced.AccountId);
        Assert.Equal("acme", announced.Username);
        Assert.Equal(["unsuspend:acme", "observe:acme"], _agent.Calls);
    }

    /// <summary>A completed suspension says on its task what it did not cover.</summary>
    /// <remarks>
    /// The task's stage line is the only place an operator is told what a suspension does NOT reach —
    /// the account's databases and the panel's own web login. A completion without it would be the
    /// panel implying a cascade wider than the one it has, which is the defect three documents in
    /// this repository already had.
    /// </remarks>
    [Fact]
    public async Task A_completed_suspension_says_on_its_task_what_it_did_not_cover()
    {
        var account = await SeedAsync();

        await SuspendAsync(_agent, new StubMessageBus(), account.Id);

        var task = Assert.Single(_tasks.Tasks);
        Assert.True(task.Completed);
        var attestation = Assert.Single(task.Reports, report => { return report.Line.Contains("NOT covered"); });
        Assert.Contains("databases", attestation.Line, StringComparison.Ordinal);
        Assert.Contains("web login", attestation.Line, StringComparison.Ordinal);
    }

    /// <summary>A completed suspension names the crontab lines it did not silence.</summary>
    /// <remarks>
    /// The panel does not touch a line it did not write — a crontab is not its file — so such a line
    /// keeps firing under a suspended account. Reporting the number is the difference between saying
    /// what was silenced and claiming a silence that was not achieved. Its inverse control is
    /// <see cref="A_completed_suspension_says_on_its_task_what_it_did_not_cover"/>, where the same
    /// line carries no such clause because the host reported none.
    /// </remarks>
    [Fact]
    public async Task A_completed_suspension_names_the_crontab_lines_it_did_not_silence()
    {
        var account = await SeedAsync();
        _agent.SuspensionState = new AccountSuspensionStateDto(
            true, AccountLoginPasswordState.Locked, true, [], 2, 2, 3, []);

        await SuspendAsync(_agent, new StubMessageBus(), account.Id);

        var task = Assert.Single(_tasks.Tasks);
        Assert.True(task.Completed);
        var attestation = Assert.Single(task.Reports, report => { return report.Line.Contains("NOT covered"); });
        Assert.Contains("3 crontab line(s) the panel did not write", attestation.Line, StringComparison.Ordinal);
    }

    /// <summary>A suspension is refused while any of the account's cron entries can still be run.</summary>
    /// <remarks>
    /// <para>
    /// Counted out of the crontab itself, because this panel keeps no cron rows at all: the Cron
    /// module owns no entity, so a check that consulted the database would be green over a firing
    /// crontab — the same defect this attestation exists to close, in a new place.
    /// </para>
    /// <para>
    /// Its inverse control is <see cref="Suspending_an_active_account_moves_it_into_suspension"/>,
    /// where an account whose two entries are both suppressed is accepted.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_suspension_is_refused_while_one_of_the_accounts_cron_entries_can_still_run()
    {
        var account = await SeedAsync();
        _agent.SuspensionState = new AccountSuspensionStateDto(
            true, AccountLoginPasswordState.Locked, true, [], 2, 1, 0, []);

        var result = await SuspendAsync(_agent, new StubMessageBus(), account.Id);

        Assert.Equal("AccountSuspensionNotObserved", result.Error!.Code);
        Assert.Equal(AccountStatus.Active, (await _context.Accounts.SingleAsync()).Status);
    }

    /// <summary>An account whose cron entries are all suppressed is accepted.</summary>
    /// <remarks>
    /// The inverse control the refusal above owes: a check that refused whatever it was shown would
    /// satisfy that test and fail this one. The counts here are non-zero on purpose — an account with
    /// no entries at all would make both zero and pass either implementation.
    /// </remarks>
    [Fact]
    public async Task An_account_whose_cron_entries_are_all_suppressed_is_accepted()
    {
        var account = await SeedAsync();
        _agent.SuspensionState = new AccountSuspensionStateDto(
            true, AccountLoginPasswordState.Locked, true, [], 2, 2, 0, []);

        var result = await SuspendAsync(_agent, new StubMessageBus(), account.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountStatus.Suspended, (await _context.Accounts.SingleAsync()).Status);
    }

    /// <summary>A suspension is refused while one of the account's SFTP logins still authenticates.</summary>
    /// <remarks>
    /// <para>
    /// The sharpest of the four conditions. An SFTP login is its own passwd entry sharing the
    /// account's uid, so the <c>usermod --lock</c> the agent performs on the account reaches none of
    /// them — and a login that still authenticates is a live WRITE credential into the customer's
    /// home, not a page a visitor sees.
    /// </para>
    /// <para>
    /// The refusal names the login, because an operator sent to look needs to know which one. The set
    /// is the HOST's, not this panel's rows: a login the panel has forgotten is exactly the one that
    /// would keep working.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_suspension_is_refused_while_one_of_the_accounts_sftp_logins_still_authenticates()
    {
        var account = await SeedAsync();
        _agent.SuspensionState = new AccountSuspensionStateDto(
            true,
            AccountLoginPasswordState.Locked,
            true,
            [],
            0,
            0,
            0,
            [new FileTransferLoginSuspensionFactDto("acme_web", true),
             new FileTransferLoginSuspensionFactDto("acme_deploy", false)]);

        var result = await SuspendAsync(_agent, new StubMessageBus(), account.Id);

        Assert.Equal("AccountSuspensionNotObserved", result.Error!.Code);
        Assert.Equal(AccountStatus.Active, (await _context.Accounts.SingleAsync()).Status);
    }

    /// <summary>An account whose SFTP logins are all locked is accepted.</summary>
    /// <remarks>
    /// The inverse control the refusal above owes, with a non-empty list on purpose: an account with
    /// no logins at all would pass either implementation.
    /// </remarks>
    [Fact]
    public async Task An_account_whose_sftp_logins_are_all_locked_is_accepted()
    {
        var account = await SeedAsync();
        _agent.SuspensionState = new AccountSuspensionStateDto(
            true, AccountLoginPasswordState.Locked, true, [], 0, 0, 0,
            [new FileTransferLoginSuspensionFactDto("acme_web", true)]);

        var result = await SuspendAsync(_agent, new StubMessageBus(), account.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountStatus.Suspended, (await _context.Accounts.SingleAsync()).Status);
    }

    /// <summary>A reactivation is refused while the crontab still carries the suspension marker.</summary>
    /// <remarks>
    /// The mirror of the cron refusal, and it is what stops a reactivation reporting a restart that
    /// left every scheduled job silent. Leaving the account suspended is the recoverable direction.
    /// </remarks>
    [Fact]
    public async Task A_reactivation_is_refused_while_the_crontab_still_carries_the_suspension_marker()
    {
        var account = await SeedAsync();
        await SuspendAsync(_agent, new StubMessageBus(), account.Id);
        _agent.SuspensionState = new AccountSuspensionStateDto(
            false, AccountLoginPasswordState.Usable, true, [], 2, 2, 0, []);

        var result = await ReactivateAsync(_agent, new StubMessageBus(), account.Id);

        Assert.Equal("AccountResumptionNotObserved", result.Error!.Code);
        Assert.Equal(AccountStatus.Suspended, (await _context.Accounts.SingleAsync()).Status);
    }

    /// <summary>A reactivation is refused while one of the account's SFTP logins is still locked.</summary>
    /// <remarks>
    /// Including the one honest false refusal this produces, which is documented on the handler: a
    /// login that never had a password cannot be unlocked, so it reads locked for ever. Refusing
    /// sends an operator to look at it rather than reporting a restoration that did not happen.
    /// </remarks>
    [Fact]
    public async Task A_reactivation_is_refused_while_one_of_the_accounts_sftp_logins_is_still_locked()
    {
        var account = await SeedAsync();
        await SuspendAsync(_agent, new StubMessageBus(), account.Id);
        _agent.SuspensionState = new AccountSuspensionStateDto(
            false, AccountLoginPasswordState.Usable, true, [], 0, 0, 0,
            [new FileTransferLoginSuspensionFactDto("acme_web", true)]);

        var result = await ReactivateAsync(_agent, new StubMessageBus(), account.Id);

        Assert.Equal("AccountResumptionNotObserved", result.Error!.Code);
        Assert.Equal(AccountStatus.Suspended, (await _context.Accounts.SingleAsync()).Status);
    }

    /// <summary>Reactivating an account whose login never had a password succeeds.</summary>
    /// <remarks>
    /// <para>
    /// The INVERSE CONTROL for this handler's login gate, and the defect it was written for: every
    /// hosting account is passwordless, <c>passwd -S</c> answers "locked" for such a login for ever,
    /// and the handler refused on that boolean — so it refused EVERY hosting account, on both
    /// families, however completely the resumption had worked. A refusing gate that is only ever fed
    /// what it must reject passes just as well when it rejects everything.
    /// </para>
    /// <para>
    /// The double is the point of the test: <see cref="RecordingAgentAccountsClient"/> clears
    /// <c>LoginLocked</c> when unsuspend succeeds, which no real host does for a login with no hash,
    /// and that is why every existing test here was green over the defect.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_reactivation_succeeds_for_an_account_whose_login_never_had_a_password()
    {
        var account = await SeedAsync();
        var host = FixedObservationAgentAccountsClient.ForPasswordlessAccount();
        await SuspendAsync(host, new StubMessageBus(), account.Id);

        var result = await ReactivateAsync(host, new StubMessageBus(), account.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountStatus.Active, (await _context.Accounts.SingleAsync()).Status);
    }

    /// <summary>A reactivation is refused while the login is still locked over a real password.</summary>
    /// <remarks>
    /// The half the inverse control above does not cover, and the one that stops the repair from
    /// becoming "never look at the login again": a hash behind a lock marker is the one state in
    /// which something of the customer's really is held down, and seeing it after the resumption
    /// means the unlock did not happen. Refusing leaves the account suspended, which is the
    /// recoverable direction.
    /// </remarks>
    [Fact]
    public async Task A_reactivation_is_refused_while_the_login_is_still_locked_over_a_password()
    {
        var account = await SeedAsync();
        await SuspendAsync(_agent, new StubMessageBus(), account.Id);
        var host = new FixedObservationAgentAccountsClient(new AccountSuspensionStateDto(
            true, AccountLoginPasswordState.Locked, true, [], 0, 0, 0, []));

        var result = await ReactivateAsync(host, new StubMessageBus(), account.Id);

        Assert.Equal("AccountResumptionNotObserved", result.Error!.Code);
        Assert.Equal(AccountStatus.Suspended, (await _context.Accounts.SingleAsync()).Status);
    }

    /// <summary>An agent that predates the password-state field still refuses on its boolean.</summary>
    /// <remarks>
    /// Version skew, and it must fail closed. An agent that sends nothing sends
    /// <see cref="AccountLoginPasswordState.Unspecified"/>, and both handlers then fall back to
    /// <c>LoginLocked</c> — the behaviour that existed before the field, which refuses in both
    /// directions. A skewed pair is no worse off than before and never more permissive; inventing a
    /// permission from an answer nobody gave is the failure this asserts against.
    /// </remarks>
    [Fact]
    public async Task A_reactivation_is_refused_when_the_agent_reports_no_password_state_and_a_locked_login()
    {
        var account = await SeedAsync();
        await SuspendAsync(_agent, new StubMessageBus(), account.Id);
        var host = new FixedObservationAgentAccountsClient(new AccountSuspensionStateDto(
            true, AccountLoginPasswordState.Unspecified, true, [], 0, 0, 0, []));

        var result = await ReactivateAsync(host, new StubMessageBus(), account.Id);

        Assert.Equal("AccountResumptionNotObserved", result.Error!.Code);
        Assert.Equal(AccountStatus.Suspended, (await _context.Accounts.SingleAsync()).Status);
    }

    /// <summary>A suspension is refused while the login authenticates with the empty password.</summary>
    /// <remarks>
    /// The state <c>passwd -S</c> calls <c>NP</c> and this attestation calls
    /// <see cref="AccountLoginPasswordState.Empty"/>: a login with an EMPTY shadow field
    /// authenticates with no password at all, which is the one thing a suspension must never leave
    /// behind. It is also exactly what <c>passwd -u -f</c> produces on the RHEL family, which is why
    /// that command is used nowhere in this product.
    /// </remarks>
    [Fact]
    public async Task A_suspension_is_refused_while_the_login_authenticates_with_the_empty_password()
    {
        var account = await SeedAsync();
        var host = new FixedObservationAgentAccountsClient(new AccountSuspensionStateDto(
            true, AccountLoginPasswordState.Empty, true, [], 0, 0, 0, []));

        var result = await SuspendAsync(host, new StubMessageBus(), account.Id);

        Assert.Equal("AccountSuspensionNotObserved", result.Error!.Code);
        Assert.Equal(AccountStatus.Active, (await _context.Accounts.SingleAsync()).Status);
    }

    /// <summary>A suspension is refused while the login still has a usable password hash.</summary>
    /// <remarks>
    /// The suspension gate asked of the shadow field rather than of <c>passwd -S</c>. It is the same
    /// refusal the boolean already produced, asserted through the new field so that a future edit
    /// cannot quietly drop <see cref="AccountLoginPasswordState.Usable"/> out of the refusing set.
    /// </remarks>
    [Fact]
    public async Task A_suspension_is_refused_while_the_login_still_has_a_usable_password()
    {
        var account = await SeedAsync();
        var host = new FixedObservationAgentAccountsClient(new AccountSuspensionStateDto(
            true, AccountLoginPasswordState.Usable, true, [], 0, 0, 0, []));

        var result = await SuspendAsync(host, new StubMessageBus(), account.Id);

        Assert.Equal("AccountSuspensionNotObserved", result.Error!.Code);
        Assert.Equal(AccountStatus.Active, (await _context.Accounts.SingleAsync()).Status);
    }

    /// <summary>A suspension is observed for an account whose login never had a password.</summary>
    /// <remarks>
    /// The inverse control for the SUSPENSION gate, and the reason the change cannot have made
    /// suspension permissive by accident: the ordinary hosting account, whose shadow field holds
    /// lock markers and no hash, is accepted — nothing can authenticate against it — while the two
    /// states that can authenticate are refused by the two tests above.
    /// </remarks>
    [Fact]
    public async Task A_suspension_is_observed_for_an_account_whose_login_never_had_a_password()
    {
        var account = await SeedAsync();
        var host = FixedObservationAgentAccountsClient.ForPasswordlessAccount();

        var result = await SuspendAsync(host, new StubMessageBus(), account.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountStatus.Suspended, (await _context.Accounts.SingleAsync()).Status);
    }

    /// <summary>A refused suspension closes its task under the code the caller was answered with.</summary>
    [Fact]
    public async Task A_refused_suspension_closes_its_task_under_the_code_the_caller_was_answered_with()
    {
        var account = await SeedAsync();
        _agent.SuspensionState = new AccountSuspensionStateDto(
            true, AccountLoginPasswordState.Locked, false, [], 0, 0, 0, []);

        var result = await SuspendAsync(_agent, new StubMessageBus(), account.Id);

        var task = Assert.Single(_tasks.Tasks);
        Assert.False(task.Completed);
        Assert.Equal(result.Error!.Code, task.FailureCode);
    }

    /// <summary>A completed suspension states, word for word, what the host was seen to have done.</summary>
    /// <remarks>
    /// <para>
    /// The WHOLE line, not a substring of it. This sentence is the operator's record that the panel
    /// did what it claims, and every earlier assertion on it looked for a fragment — "NOT covered",
    /// "databases" — that a wrong sentence carries just as well. Two defects lived under exactly that
    /// kind of check: the line counted a mixed list of transfer logins as "sftp logins", and it
    /// swallowed the count of logins the panel does not own entirely.
    /// </para>
    /// <para>
    /// The host here holds one login of each daemon, so a count that lumped them together would read
    /// "all 2 of its sftp logins" and fail, and two uid-sharing logins the panel never created, so a
    /// line that omits them fails too.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_completed_suspension_states_word_for_word_what_the_host_was_seen_to_have_done()
    {
        var account = await SeedAsync();
        _agent.SuspensionState = new AccountSuspensionStateDto(
            true,
            AccountLoginPasswordState.Locked,
            true,
            [],
            2,
            2,
            0,
            [new FileTransferLoginSuspensionFactDto("acme_web", true, LoginTransferProtocol.Sftp),
             new FileTransferLoginSuspensionFactDto("acme_files", true, LoginTransferProtocol.Ftps)],
            2);

        await SuspendAsync(_agent, new StubMessageBus(), account.Id);

        var task = Assert.Single(_tasks.Tasks);
        Assert.True(task.Completed);
        var attestation = Assert.Single(task.Reports, report => { return report.Line.Contains("NOT covered"); });
        Assert.Equal(
            "the host shows the login locked, all 0 of its vhosts for this account serving the "
                + "suspended page, all 2 of its cron entries suppressed and all 1 of its sftp logins "
                + "and all 1 of its ftps logins locked; the panel asked the host to end the account's "
                + "open transfer sessions, and no module reported having done so, so this line cannot "
                + "say that any were ended; NOT covered by this suspension: "
                + "the account's databases and the panel's own web login, and 2 login(s) sharing this "
                + "account's uid that the panel did not create and does not lock",
            attestation.Line);
    }

    /// <summary>A suspension of an account with no login of either kind reads correctly, word for word.</summary>
    /// <remarks>
    /// <para>
    /// The case a live panel printed, and the case three separate defects met in. The account has no
    /// login of either kind, no vhost and no cron entry — the commonest account there is — and the
    /// line used to name SFTP and not FTPS, answer "unknown rather than zero" where no operator could
    /// act on it, and say nothing whatever about the sessions the suspension had just killed.
    /// </para>
    /// <para>
    /// Asserted as the WHOLE line. Every wrong answer here is a substring relationship away from a
    /// right one: "all 0 of its sftp logins" is a prefix of the corrected phrase, and both unmeasured
    /// wordings contain the word uid.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_suspension_of_an_account_with_no_login_of_either_kind_reads_correctly_word_for_word()
    {
        var account = await SeedAsync();
        _agent.SuspensionState = new AccountSuspensionStateDto(
            true, AccountLoginPasswordState.Locked, true, [], 0, 0, 0, []);

        await SuspendAsync(_agent, new StubMessageBus(), account.Id);

        var task = Assert.Single(_tasks.Tasks);
        Assert.True(task.Completed);
        var attestation = Assert.Single(task.Reports, report => { return report.Line.Contains("NOT covered"); });
        Assert.Equal(
            "the host shows the login locked, all 0 of its vhosts for this account serving the "
                + "suspended page, all 0 of its cron entries suppressed and all 0 of its sftp logins "
                + "and all 0 of its ftps logins locked; the panel asked the host to end the account's "
                + "open transfer sessions, and no module reported having done so, so this line cannot "
                + "say that any were ended; NOT covered by this suspension: "
                + "the account's databases and the panel's own web login, and no transfer login was "
                + "reported, so nothing in the host's answer can say whether a login outside the "
                + "panel's own shares this account's uid: read the host's passwd for entries carrying "
                + "it",
            attestation.Line);
    }

    /// <summary>A completed suspension whose cascade nobody answered says only that the panel asked.</summary>
    /// <remarks>
    /// <para>
    /// Ending the customer's open transfer sessions is a privileged action of a suspension, so it
    /// belongs on the operator's record of what the panel did (rules/security.md). It was missing
    /// entirely, and an attestation that omits an action reads as complete — which is worse than none.
    /// </para>
    /// <para>
    /// This is the reading when no subscriber reported: the bus double records the cascade and runs
    /// nothing, which is the honest double for a panel where no module that ends sessions was composed.
    /// The clause is asserted WHOLE, because the danger here is the opposite of silence — a line
    /// claiming the sessions were ended, or implying a number, would state something nobody told this
    /// module, and the panel has already promised the operator before the act that a running transfer
    /// is cut off.
    /// </para>
    /// <para>
    /// It is also the inverse control for the two tests below, which hand the cascade a subscriber that
    /// answers: a policy that ignored the report and printed one sentence for ever would satisfy this
    /// test and fail those.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_completed_suspension_whose_cascade_nobody_answered_says_only_that_the_panel_asked()
    {
        var account = await SeedAsync();
        _agent.SuspensionState = new AccountSuspensionStateDto(
            true,
            AccountLoginPasswordState.Locked,
            true,
            [],
            0,
            0,
            0,
            [new FileTransferLoginSuspensionFactDto("acme_web", true, LoginTransferProtocol.Sftp)],
            0);

        await SuspendAsync(_agent, new StubMessageBus(), account.Id);

        var task = Assert.Single(_tasks.Tasks);
        var attestation = Assert.Single(task.Reports, report => { return report.Line.Contains("NOT covered"); });
        Assert.Contains(
            "the panel asked the host to end the account's open transfer sessions, and no module "
                + "reported having done so, so this line cannot say that any were ended",
            attestation.Line,
            StringComparison.Ordinal);
    }

    /// <summary>A count a subscriber reported is stated on the attestation as a number.</summary>
    /// <remarks>
    /// The end of the disagreement this lane existed to close: the panel warns the operator before a
    /// suspension that a running transfer is cut off and the partial file stays, and until the rpc
    /// carried a count its own record could only say it had ASKED. The promise and the record now say
    /// the same thing. The number travels agent → <c>optional uint32 sessions_ended</c> → the Sftp
    /// subscriber → <c>AccountSuspending.Report</c> → this line, and this test is the only one that
    /// observes the whole of that path inside the panel.
    /// </remarks>
    [Fact]
    public async Task A_count_a_subscriber_reported_is_stated_on_the_attestation_as_a_number()
    {
        var account = await SeedAsync();
        _agent.SuspensionState = new AccountSuspensionStateDto(
            true, AccountLoginPasswordState.Locked, true, [], 0, 0, 0, []);
        var bus = new StubMessageBus(message =>
        {
            ((AccountSuspending)message).Report.ReportSessionCull(3);
        });

        await SuspendAsync(_agent, bus, account.Id);

        var task = Assert.Single(_tasks.Tasks);
        Assert.True(task.Completed);
        var attestation = Assert.Single(task.Reports, report => { return report.Line.Contains("NOT covered"); });
        Assert.Contains(
            "a suspension also ends the account's open transfer sessions, and the host ended 3 of "
                + "them, cutting whatever they were transferring and leaving any partial file in the "
                + "account's home",
            attestation.Line,
            StringComparison.Ordinal);
    }

    /// <summary>A subscriber that answered with no count leaves the line saying the host did not say.</summary>
    /// <remarks>
    /// The vacuity guard for the test above, on the axis that can actually go blind. A subscriber that
    /// answered and a count that arrived are two facts, and the second is absent whenever the agent
    /// predates the wire field; a line that printed a number here would be inventing one, and a line
    /// that said "no module reported" would blame the cascade for the agent's age. Three wordings, and
    /// this test is what keeps the middle one reachable.
    /// </remarks>
    [Fact]
    public async Task A_subscriber_that_answered_with_no_count_leaves_the_line_saying_the_host_did_not_say()
    {
        var account = await SeedAsync();
        _agent.SuspensionState = new AccountSuspensionStateDto(
            true, AccountLoginPasswordState.Locked, true, [], 0, 0, 0, []);
        var bus = new StubMessageBus(message =>
        {
            ((AccountSuspending)message).Report.ReportSessionCull(null);
        });

        await SuspendAsync(_agent, bus, account.Id);

        var attestation = Assert.Single(
            Assert.Single(_tasks.Tasks).Reports,
            report => { return report.Line.Contains("NOT covered"); });
        Assert.Contains(
            "a suspension also ends the account's open transfer sessions, and the host's answer "
                + "carried no count of them, so this line cannot say how many were ended, or that any "
                + "were",
            attestation.Line,
            StringComparison.Ordinal);
        Assert.DoesNotContain("no module reported", attestation.Line, StringComparison.Ordinal);
    }

    /// <summary>A completed suspension says the host counted no login it does not own.</summary>
    /// <remarks>
    /// The measured zero, which is a completeness claim: the host looked and there was nothing to
    /// report. It is stated rather than left to silence, because silence is what a host that never
    /// answered also produces — see the test below, which is this one's vacuity guard.
    /// </remarks>
    [Fact]
    public async Task A_completed_suspension_says_the_host_counted_no_login_it_does_not_own()
    {
        var account = await SeedAsync();
        _agent.SuspensionState = new AccountSuspensionStateDto(
            true,
            AccountLoginPasswordState.Locked,
            true,
            [],
            0,
            0,
            0,
            [new FileTransferLoginSuspensionFactDto("acme_web", true, LoginTransferProtocol.Sftp)],
            0);

        await SuspendAsync(_agent, new StubMessageBus(), account.Id);

        var task = Assert.Single(_tasks.Tasks);
        var attestation = Assert.Single(task.Reports, report => { return report.Line.Contains("NOT covered"); });
        Assert.EndsWith(
            ", and no login outside the panel's own shares this account's uid",
            attestation.Line,
            StringComparison.Ordinal);
    }

    /// <summary>A completed suspension calls a count the host never gave unknown rather than zero.</summary>
    /// <remarks>
    /// <para>
    /// The vacuity guard for the test above, on the axis that can go blind. The wire carries a
    /// <c>uint32</c>, so an agent that predates the count sends the same zero as one that counted and
    /// found none — and the reading that says "none" over an answer nobody gave is the completeness
    /// claim this whole attestation exists to stop the panel making.
    /// </para>
    /// <para>
    /// The only difference between this arrangement and the one above is that the reported login
    /// carries no protocol, which is how such an agent is recognised. A handler that read the number
    /// alone would produce the same sentence for both and pass exactly one of the two.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_completed_suspension_calls_a_count_the_host_never_gave_unknown_rather_than_zero()
    {
        var account = await SeedAsync();
        _agent.SuspensionState = new AccountSuspensionStateDto(
            true,
            AccountLoginPasswordState.Locked,
            true,
            [],
            0,
            0,
            0,
            [new FileTransferLoginSuspensionFactDto("acme_web", true)],
            0);

        await SuspendAsync(_agent, new StubMessageBus(), account.Id);

        var task = Assert.Single(_tasks.Tasks);
        var attestation = Assert.Single(task.Reports, report => { return report.Line.Contains("NOT covered"); });
        Assert.EndsWith(
            ", and the host did not say how many logins share this account's uid without the panel "
                + "having created them, so that number is unknown rather than zero",
            attestation.Line,
            StringComparison.Ordinal);
    }

    /// <summary>A refused suspension names the open login together with the daemon that serves it.</summary>
    /// <remarks>
    /// This sentence reaches the operator through the log and nowhere else, so it is asserted through
    /// a recording logger rather than by proving a refusal happened: a refusal with an unusable
    /// reason refuses just as convincingly. The two open logins share a name shape and differ only in
    /// their daemon, which is the fact an operator needs before they know which client to open.
    /// </remarks>
    [Fact]
    public async Task A_refused_suspension_names_the_open_login_together_with_the_daemon_that_serves_it()
    {
        var account = await SeedAsync();
        var logger = new RecordingLogger<SuspendAccountCommandHandler>();
        _agent.SuspensionState = new AccountSuspensionStateDto(
            true,
            AccountLoginPasswordState.Locked,
            true,
            [],
            0,
            0,
            0,
            [new FileTransferLoginSuspensionFactDto("acme_web", false, LoginTransferProtocol.Ftps),
             new FileTransferLoginSuspensionFactDto("acme_deploy", true, LoginTransferProtocol.Sftp)],
            0);

        var result = await SuspendAsync(_agent, new StubMessageBus(), account.Id, logger);

        Assert.Equal("AccountSuspensionNotObserved", result.Error!.Code);
        Assert.Equal(
            "Account acme was not suspended: the host does not show it stopped — these transfer "
                + "logins still authenticate: acme_web (ftps).",
            Assert.Single(logger.Messages));
    }

    /// <summary>A completed reactivation states, word for word, what the host was seen to have restored.</summary>
    /// <remarks>
    /// The resumption's half of the same wording defect: this line counted the same mixed list of
    /// transfer logins under the SFTP daemon's name, and an operator reading it believed their
    /// customer's FTP credentials had not been touched — while the resumption had in fact unlocked
    /// them. The host here holds one login of each daemon, so a lumped count fails.
    /// </remarks>
    [Fact]
    public async Task A_completed_reactivation_states_word_for_word_what_the_host_was_seen_to_have_restored()
    {
        var account = await SeedAsync();
        await SuspendAsync(_agent, new StubMessageBus(), account.Id);
        _agent.SuspensionState = new AccountSuspensionStateDto(
            false,
            AccountLoginPasswordState.Usable,
            true,
            [],
            2,
            0,
            0,
            [new FileTransferLoginSuspensionFactDto("acme_web", false, LoginTransferProtocol.Sftp),
             new FileTransferLoginSuspensionFactDto("acme_files", false, LoginTransferProtocol.Ftps)]);
        _tasks.Tasks.Clear();

        await ReactivateAsync(_agent, new StubMessageBus(), account.Id);

        var task = Assert.Single(_tasks.Tasks);
        Assert.True(task.Completed);
        var attestation = Assert.Single(task.Reports, report => { return report.Line.Contains("never stopped"); });
        Assert.Equal(
            "the host shows the login unlocked, none of its 0 vhosts for this account serving the "
                + "suspension page, all 2 of its cron entries free to run again and all 1 of its sftp "
                + "logins and all 1 of its ftps logins unlocked; the account's databases and the "
                + "panel's own web login were never stopped by the suspension, so nothing here "
                + "restored them",
            attestation.Line);
    }

    /// <summary>A refused reactivation names the still locked login together with the daemon that serves it.</summary>
    /// <remarks>
    /// The mirror of the suspension's refusal line, and it reaches the operator through the log in
    /// the same way. The one still-locked login is an FTPS one, so a sentence that named the daemon
    /// wrongly — or not at all — would send its reader to the wrong client.
    /// </remarks>
    [Fact]
    public async Task A_refused_reactivation_names_the_still_locked_login_together_with_the_daemon_that_serves_it()
    {
        var account = await SeedAsync();
        await SuspendAsync(_agent, new StubMessageBus(), account.Id);
        var logger = new RecordingLogger<ReactivateAccountCommandHandler>();
        _agent.SuspensionState = new AccountSuspensionStateDto(
            false,
            AccountLoginPasswordState.Usable,
            true,
            [],
            0,
            0,
            0,
            [new FileTransferLoginSuspensionFactDto("acme_files", true, LoginTransferProtocol.Ftps),
             new FileTransferLoginSuspensionFactDto("acme_web", false, LoginTransferProtocol.Sftp)]);

        var result = await ReactivateAsync(_agent, new StubMessageBus(), account.Id, logger);

        Assert.Equal("AccountResumptionNotObserved", result.Error!.Code);
        Assert.Equal(
            "Account acme was not reactivated: the host does not show it running — these transfer "
                + "logins are still locked: acme_files (ftps).",
            Assert.Single(logger.Messages));
    }

    /// <summary>Seeds one active account named "acme".</summary>
    /// <returns>The seeded account.</returns>
    private async Task<Account> SeedAsync()
    {
        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), Now);
        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();
        return account;
    }

    /// <summary>Runs one suspension against the fixture's context.</summary>
    /// <param name="agent">The agent double to answer with.</param>
    /// <param name="bus">The bus the cascade is invoked on.</param>
    /// <param name="accountId">The account to suspend.</param>
    /// <returns>The handler's result.</returns>
    /// <param name="logger">
    /// Where the handler writes the reason it refused, when a test asserts that sentence; the null
    /// logger otherwise, which is every test whose subject is the outcome rather than the wording.
    /// </param>
    private Task<Result<AccountDto>> SuspendAsync(
        IAgentAccountsClient agent,
        StubMessageBus bus,
        Guid accountId,
        ILogger<SuspendAccountCommandHandler>? logger = null)
    {
        var handler = new SuspendAccountCommandHandler(
            _context,
            agent,
            bus,
            logger ?? NullLogger<SuspendAccountCommandHandler>.Instance,
            Journal(),
            _tasks,
            new StubCorrelationIdAccessor("corr-1"));

        return handler.HandleAsync(new SuspendAccountCommand(accountId, Ip, Client), CancellationToken.None);
    }

    /// <summary>Runs one reactivation against the fixture's context.</summary>
    /// <param name="agent">The agent double to answer with.</param>
    /// <param name="bus">The bus the cascade is invoked on.</param>
    /// <param name="accountId">The account to reactivate.</param>
    /// <returns>The handler's result.</returns>
    /// <param name="logger">
    /// Where the handler writes the reason it refused, when a test asserts that sentence; the null
    /// logger otherwise.
    /// </param>
    private Task<Result<AccountDto>> ReactivateAsync(
        IAgentAccountsClient agent,
        StubMessageBus bus,
        Guid accountId,
        ILogger<ReactivateAccountCommandHandler>? logger = null)
    {
        var handler = new ReactivateAccountCommandHandler(
            _context,
            agent,
            bus,
            logger ?? NullLogger<ReactivateAccountCommandHandler>.Instance,
            Journal(),
            _tasks,
            new StubCorrelationIdAccessor("corr-1"));

        return handler.HandleAsync(new ReactivateAccountCommand(accountId, Ip, Client), CancellationToken.None);
    }
}
