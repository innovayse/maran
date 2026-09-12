using Maran.Modules.Ftp.Commands.EnableFtps;
using Maran.Modules.Ftp.Domain.Policies;
using Maran.Modules.Ftp.Services;
using Maran.Modules.Ftp.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Ftp.Tests.Commands.EnableFtps;

/// <summary>Behaviour of the handler that configures and starts this server's FTPS daemon.</summary>
public sealed class EnableFtpsCommandHandlerTests
{
    /// <summary>The hostname every test that expects success enables FTPS for.</summary>
    private const string ServedHostname = "ftp.example.test";

    /// <summary>Enabling FTPS for a host name the panel does not serve is refused with the named code.</summary>
    /// <remarks>
    /// <c>Error</c> is <c>(Code, Type)</c> and deliberately has no message — the sentence a customer
    /// reads lives in the resx, in three languages — so a refusal is asserted as its code and its
    /// kind. The no-paths guarantee is asserted where the text actually lives, in
    /// <see cref="Resources.ErrorMessagesTests"/>.
    /// </remarks>
    [Fact]
    public async Task Enabling_ftps_for_a_hostname_the_panel_does_not_serve_is_refused_with_the_named_code()
    {
        using var context = new FtpTestContext();

        var result = await context.EnableHandler.HandleAsync(
            new EnableFtpsCommand("ftp.unknown.test"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.FtpsHostnameNotServed, result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    /// <summary>A hostname the panel does not serve never reaches the agent.</summary>
    /// <remarks>
    /// The refusal above and this are two different claims. A handler that answered the right code
    /// AFTER asking the agent to configure a daemon for a name nobody can ever get a certificate for
    /// would satisfy the first test and still have started the wrong daemon.
    /// </remarks>
    [Fact]
    public async Task A_hostname_the_panel_does_not_serve_never_reaches_the_agent()
    {
        using var context = new FtpTestContext();

        await context.EnableHandler.HandleAsync(
            new EnableFtpsCommand("ftp.unknown.test"), CancellationToken.None);

        Assert.Null(context.Agent.LastEnableRequest);
    }

    /// <summary>The agent receives the range and ceiling from FtpsDefaults and never from the caller.</summary>
    /// <remarks>
    /// The command carries no numbers, so the only way these can reach the wire is the backend's own
    /// named constants — which is what keeps the panel's passive range and the firewall's the same
    /// range.
    /// </remarks>
    [Fact]
    public async Task The_agent_receives_the_range_and_ceiling_from_FtpsDefaults_and_never_from_the_caller()
    {
        using var context = new FtpTestContext();
        context.Sites.Serve(ServedHostname);

        await context.EnableHandler.HandleAsync(
            new EnableFtpsCommand(ServedHostname), CancellationToken.None);

        Assert.Equal((uint)FtpsDefaults.PassivePortMin, context.Agent.LastEnableRequest!.PassivePortMin);
        Assert.Equal((uint)FtpsDefaults.PassivePortMax, context.Agent.LastEnableRequest.PassivePortMax);
        Assert.Equal((uint)FtpsDefaults.MaxClients, context.Agent.LastEnableRequest.MaxClients);
    }

    /// <summary>The listen mode the host chose is persisted from the enable response.</summary>
    /// <remarks>The host decides, the panel reports — and remembers, so a screen can state it.</remarks>
    [Fact]
    public async Task The_listen_mode_the_host_chose_is_persisted_from_the_enable_response()
    {
        using var context = new FtpTestContext();
        context.Sites.Serve(ServedHostname);
        context.Agent.NextEnableAnswersIpv4Only();

        await context.EnableHandler.HandleAsync(
            new EnableFtpsCommand(ServedHostname), CancellationToken.None);

        var row = await context.Database.FtpsSettings.SingleAsync(CancellationToken.None);
        Assert.True(row.Ipv4Only);
    }

    /// <summary>Enabling ftps records an audit entry naming the hostname.</summary>
    [Fact]
    public async Task Enabling_ftps_records_an_audit_entry_naming_the_hostname()
    {
        using var context = new FtpTestContext();
        context.Sites.Serve(ServedHostname);

        await context.EnableHandler.HandleAsync(
            new EnableFtpsCommand(ServedHostname), CancellationToken.None);

        var entry = Assert.Single(context.Entries);
        Assert.Equal(FtpAuditJournal.FtpsEnabled, entry.Action);
        Assert.Equal(ServedHostname, entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A refused enable is journalled as a failure naming what was asked for.</summary>
    /// <remarks>
    /// Failures are the half of the journal worth reading: an operator asking why FTPS is off needs
    /// the refused attempt, and a handler that journalled only successes would show them nothing.
    /// </remarks>
    [Fact]
    public async Task A_refused_enable_is_journalled_as_a_failure_naming_what_was_asked_for()
    {
        using var context = new FtpTestContext();

        await context.EnableHandler.HandleAsync(
            new EnableFtpsCommand("ftp.unknown.test"), CancellationToken.None);

        var entry = Assert.Single(context.Entries);
        Assert.Equal(FtpAuditJournal.FtpsEnabled, entry.Action);
        Assert.Equal("ftp.unknown.test", entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A missing certificate is reported as a server failure, never as bad input.</summary>
    /// <remarks>
    /// The kind is the assertion that matters. <c>ErrorType.Validation</c> answers 400 and sends the
    /// operator back to correct a form in which nothing is wrong; the hostname is theirs, the panel
    /// serves a site for it, and the same request succeeds unchanged once certificate material
    /// exists. <c>ErrorType.Failure</c> is documented as covering a server whose own configuration is
    /// incomplete, which is this.
    /// </remarks>
    [Fact]
    public async Task A_missing_certificate_is_reported_as_a_server_failure_and_not_as_bad_input()
    {
        using var context = new FtpTestContext();
        context.Sites.Serve(ServedHostname);
        context.Agent.NextEnableError = Error.Of("AgentNotFound", ErrorType.NotFound);

        var result = await context.EnableHandler.HandleAsync(
            new EnableFtpsCommand(ServedHostname), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.FtpsCertificateMissing, result.Error!.Code);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
    }

    /// <summary>An agent failure that is not a missing certificate is passed through unchanged.</summary>
    /// <remarks>
    /// This is the inverse control on the translation above: a translator that mapped everything to
    /// <c>FtpsCertificateMissing</c> would pass the missing-certificate test and tell an operator
    /// with a broken daemon to go and issue a certificate they already have. The agent's per-account
    /// lock refuses rather than waits and the contract carries no busy code, so a concurrent
    /// operation arrives here as exactly this: <c>AgentSystemFailure</c>, passed through.
    /// </remarks>
    [Fact]
    public async Task An_agent_failure_that_is_not_a_missing_certificate_is_passed_through_unchanged()
    {
        using var context = new FtpTestContext();
        context.Sites.Serve(ServedHostname);
        context.Agent.NextEnableError = Error.Of("AgentSystemFailure", ErrorType.Failure);

        var result = await context.EnableHandler.HandleAsync(
            new EnableFtpsCommand(ServedHostname), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
    }

    /// <summary>A refused enable writes no settings row.</summary>
    /// <remarks>
    /// The agent runs before the row is written for this reason: a row saying FTPS is on with no
    /// daemon behind it is a panel telling an operator their customers can connect when they cannot.
    /// </remarks>
    [Fact]
    public async Task A_refused_enable_writes_no_settings_row()
    {
        using var context = new FtpTestContext();
        context.Sites.Serve(ServedHostname);
        context.Agent.NextEnableError = Error.Of("AgentNotFound", ErrorType.NotFound);

        await context.EnableHandler.HandleAsync(
            new EnableFtpsCommand(ServedHostname), CancellationToken.None);

        Assert.Empty(await context.Database.FtpsSettings.ToListAsync(CancellationToken.None));
    }

    /// <summary>An operator who gave no passive address sends an empty one, not a null.</summary>
    /// <remarks>
    /// The contract gives EMPTY its own meaning — the key is not written at all and the daemon
    /// answers with the address the control connection arrived on — so the absence has to arrive as
    /// the value the contract defines rather than as a null the wire cannot carry.
    /// </remarks>
    [Fact]
    public async Task An_operator_who_gave_no_passive_address_sends_an_empty_one()
    {
        using var context = new FtpTestContext();
        context.Sites.Serve(ServedHostname);

        await context.EnableHandler.HandleAsync(
            new EnableFtpsCommand(ServedHostname), CancellationToken.None);

        Assert.Equal(string.Empty, context.Agent.LastEnableRequest!.PassiveAddress);
    }

    /// <summary>The status returned by an enable reports the daemon's TLS enforcement, not the panel's wish.</summary>
    /// <remarks>
    /// The defect this exists to refuse: a daemon whose live configuration no longer forces TLS
    /// still runs, still greets and still reports certificate material, so every other field reads
    /// healthy. If the panel substituted its own intention here, the one screen able to say
    /// "passwords are crossing this server in the clear" would show green.
    /// </remarks>
    [Fact]
    public async Task The_status_reports_the_daemons_tls_enforcement_and_not_the_panels_wish()
    {
        using var context = new FtpTestContext();
        context.Sites.Serve(ServedHostname);
        context.Agent.NextStatus = context.Agent.NextStatus with { ForcedTls = false };

        var result = await context.EnableHandler.HandleAsync(
            new EnableFtpsCommand(ServedHostname), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.ForcedTls);
        Assert.True(result.Value.Enabled);
    }
}
