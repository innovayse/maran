using Maran.Agent.Client.Services.AccountsService;
using Maran.Modules.Accounts.Commands.ReactivateAccount;
using Maran.Modules.Accounts.Commands.SuspendAccount;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Events;

namespace Maran.Modules.Accounts.Tests.Commands.SuspendAccount;

/// <summary>
/// The words a completed or refused suspension and reactivation leave on the task and in the log,
/// word for word: what the host was seen to have done, what was not covered, and the code the task
/// is closed under.
/// </summary>
public sealed class AccountSuspensionTaskReportTests : IDisposable
{
    /// <summary>The shared fixture: context, doubles and the seed, suspend and reactivate helpers.</summary>
    private readonly AccountSuspensionHarness _harness = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _harness.Dispose();
    }

    /// <summary>A refused suspension closes its task under the code the caller was answered with.</summary>
    [Fact]
    public async Task A_refused_suspension_closes_its_task_under_the_code_the_caller_was_answered_with()
    {
        var account = await _harness.SeedAsync();
        _harness.Agent.SuspensionState = new AccountSuspensionStateDto(
            AccountLoginPasswordState.Locked, false, [], 0, 0, 0, [], new MeasuredUnmanagedLogins(0));

        var result = await _harness.SuspendAsync(_harness.Agent, new StubMessageBus(), account.Id);

        var task = Assert.Single(_harness.Tasks.Tasks);
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
        var account = await _harness.SeedAsync();
        _harness.Agent.SuspensionState = new AccountSuspensionStateDto(
            AccountLoginPasswordState.Locked,
            true,
            [],
            2,
            2,
            0,
            [new FileTransferLoginSuspensionFactDto("acme_web", true, LoginTransferProtocol.Sftp),
             new FileTransferLoginSuspensionFactDto("acme_files", true, LoginTransferProtocol.Ftps)],
            new MeasuredUnmanagedLogins(2));

        await _harness.SuspendAsync(_harness.Agent, new StubMessageBus(), account.Id);

        var task = Assert.Single(_harness.Tasks.Tasks);
        Assert.True(task.Completed);
        var attestation = Assert.Single(task.Reports, report => { return report.Line.Contains("NOT covered"); });
        Assert.Equal(
            "the host shows the login locked, all 0 of its vhosts for this account serving the "
                + "suspended page, all 2 of its cron entries suppressed and all 1 of its sftp logins "
                + "and all 1 of its ftps logins locked; the panel asked the host to end the account's "
                + "open transfer sessions, and no module reported having done so, so this line cannot "
                + "say that any were ended; NOT covered by this suspension: "
                + "the account's databases, and 2 login(s) sharing this "
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
        var account = await _harness.SeedAsync();
        _harness.Agent.SuspensionState = new AccountSuspensionStateDto(
            AccountLoginPasswordState.Locked, true, [], 0, 0, 0, [], new UnmeasuredUnmanagedLogins());

        await _harness.SuspendAsync(_harness.Agent, new StubMessageBus(), account.Id);

        var task = Assert.Single(_harness.Tasks.Tasks);
        Assert.True(task.Completed);
        var attestation = Assert.Single(task.Reports, report => { return report.Line.Contains("NOT covered"); });
        Assert.Equal(
            "the host shows the login locked, all 0 of its vhosts for this account serving the "
                + "suspended page, all 0 of its cron entries suppressed and all 0 of its sftp logins "
                + "and all 0 of its ftps logins locked; the panel asked the host to end the account's "
                + "open transfer sessions, and no module reported having done so, so this line cannot "
                + "say that any were ended; NOT covered by this suspension: "
                + "the account's databases, and no transfer login was "
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
        var account = await _harness.SeedAsync();
        _harness.Agent.SuspensionState = new AccountSuspensionStateDto(
            AccountLoginPasswordState.Locked,
            true,
            [],
            0,
            0,
            0,
            [new FileTransferLoginSuspensionFactDto("acme_web", true, LoginTransferProtocol.Sftp)],
            new MeasuredUnmanagedLogins(0));

        await _harness.SuspendAsync(_harness.Agent, new StubMessageBus(), account.Id);

        var task = Assert.Single(_harness.Tasks.Tasks);
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
        var account = await _harness.SeedAsync();
        _harness.Agent.SuspensionState = new AccountSuspensionStateDto(
            AccountLoginPasswordState.Locked, true, [], 0, 0, 0, [], new MeasuredUnmanagedLogins(0));
        var bus = new StubMessageBus(message =>
        {
            ((AccountSuspending)message).Report.ReportSessionCull(3);
        });

        await _harness.SuspendAsync(_harness.Agent, bus, account.Id);

        var task = Assert.Single(_harness.Tasks.Tasks);
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
        var account = await _harness.SeedAsync();
        _harness.Agent.SuspensionState = new AccountSuspensionStateDto(
            AccountLoginPasswordState.Locked, true, [], 0, 0, 0, [], new MeasuredUnmanagedLogins(0));
        var bus = new StubMessageBus(message =>
        {
            ((AccountSuspending)message).Report.ReportSessionCull(null);
        });

        await _harness.SuspendAsync(_harness.Agent, bus, account.Id);

        var attestation = Assert.Single(
            Assert.Single(_harness.Tasks.Tasks).Reports,
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
        var account = await _harness.SeedAsync();
        _harness.Agent.SuspensionState = new AccountSuspensionStateDto(
            AccountLoginPasswordState.Locked,
            true,
            [],
            0,
            0,
            0,
            [new FileTransferLoginSuspensionFactDto("acme_web", true, LoginTransferProtocol.Sftp)],
            new MeasuredUnmanagedLogins(0));

        await _harness.SuspendAsync(_harness.Agent, new StubMessageBus(), account.Id);

        var task = Assert.Single(_harness.Tasks.Tasks);
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
    /// The only difference between this arrangement and the one above is the count's case: the agent
    /// client carries a zero as unmeasured when the raw answer cannot prove the agent counted (no
    /// login named its daemon). A handler that read the number alone would produce the same sentence
    /// for both and pass exactly one of the two.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_completed_suspension_calls_a_count_the_host_never_gave_unknown_rather_than_zero()
    {
        var account = await _harness.SeedAsync();
        _harness.Agent.SuspensionState = new AccountSuspensionStateDto(
            AccountLoginPasswordState.Locked,
            true,
            [],
            0,
            0,
            0,
            [new FileTransferLoginSuspensionFactDto("acme_web", true, LoginTransferProtocol.Sftp)],
            new UnmeasuredUnmanagedLogins());

        await _harness.SuspendAsync(_harness.Agent, new StubMessageBus(), account.Id);

        var task = Assert.Single(_harness.Tasks.Tasks);
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
        var account = await _harness.SeedAsync();
        var logger = new RecordingLogger<SuspendAccountCommandHandler>();
        _harness.Agent.SuspensionState = new AccountSuspensionStateDto(
            AccountLoginPasswordState.Locked,
            true,
            [],
            0,
            0,
            0,
            [new FileTransferLoginSuspensionFactDto("acme_web", false, LoginTransferProtocol.Ftps),
             new FileTransferLoginSuspensionFactDto("acme_deploy", true, LoginTransferProtocol.Sftp)],
            new MeasuredUnmanagedLogins(0));

        var result = await _harness.SuspendAsync(_harness.Agent, new StubMessageBus(), account.Id, logger);

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
        var account = await _harness.SeedAsync();
        await _harness.SuspendAsync(_harness.Agent, new StubMessageBus(), account.Id);
        _harness.Agent.SuspensionState = new AccountSuspensionStateDto(
            AccountLoginPasswordState.Usable,
            true,
            [],
            2,
            0,
            0,
            [new FileTransferLoginSuspensionFactDto("acme_web", false, LoginTransferProtocol.Sftp),
             new FileTransferLoginSuspensionFactDto("acme_files", false, LoginTransferProtocol.Ftps)], new MeasuredUnmanagedLogins(0));
        _harness.Tasks.Tasks.Clear();

        await _harness.ReactivateAsync(_harness.Agent, new StubMessageBus(), account.Id);

        var task = Assert.Single(_harness.Tasks.Tasks);
        Assert.True(task.Completed);
        var attestation = Assert.Single(task.Reports, report => { return report.Line.Contains("never stopped"); });
        Assert.Equal(
            "the host shows the login unlocked, none of its 0 vhosts for this account serving the "
                + "suspension page, all 2 of its cron entries free to run again and all 1 of its sftp "
                + "logins and all 1 of its ftps logins unlocked; the account's databases were never "
                + "stopped by the suspension, so nothing here restored them",
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
        var account = await _harness.SeedAsync();
        await _harness.SuspendAsync(_harness.Agent, new StubMessageBus(), account.Id);
        var logger = new RecordingLogger<ReactivateAccountCommandHandler>();
        _harness.Agent.SuspensionState = new AccountSuspensionStateDto(
            AccountLoginPasswordState.Usable,
            true,
            [],
            0,
            0,
            0,
            [new FileTransferLoginSuspensionFactDto("acme_files", true, LoginTransferProtocol.Ftps),
             new FileTransferLoginSuspensionFactDto("acme_web", false, LoginTransferProtocol.Sftp)], new MeasuredUnmanagedLogins(0));

        var result = await _harness.ReactivateAsync(_harness.Agent, new StubMessageBus(), account.Id, logger);

        Assert.Equal("AccountResumptionNotObserved", result.Error!.Code);
        Assert.Equal(
            "Account acme was not reactivated: the host does not show it running — these transfer "
                + "logins are still locked: acme_files (ftps).",
            Assert.Single(logger.Messages));
    }
}
