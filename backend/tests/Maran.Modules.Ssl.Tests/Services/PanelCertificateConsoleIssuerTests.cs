using Maran.Modules.Ssl.Models;
using Maran.Modules.Ssl.Services;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Ssl.Tests.Services;

/// <summary>The console issuance of the panel's own certificate, driven with the authority faked.</summary>
public sealed class PanelCertificateConsoleIssuerTests
{
    /// <summary>The panel host name every test here is configured with.</summary>
    private const string PanelName = "panel.example.net";

    /// <summary>The operator every test claims to be.</summary>
    private const string Operator = "alice";

    /// <summary>Builds the service over the fixture with the given panel-name window.</summary>
    private static PanelCertificateConsoleIssuer IssuerFor(SslHandlerFixture fixture, StubPanelHostName name)
    {
        return new PanelCertificateConsoleIssuer(
            name,
            new PanelCertificateIssuer(fixture.DbContext, fixture.Acme, fixture.Agent, fixture.Clock),
            fixture.Journal);
    }

    /// <summary>Issuing orders for the configured name, installs the pair and records the row.</summary>
    [Fact]
    public async Task Issuing_orders_for_the_configured_name_installs_the_pair_and_records_the_row()
    {
        using var fixture = new SslHandlerFixture([]);

        var result = await IssuerFor(fixture, new StubPanelHostName(PanelName)).IssueAsync(Operator, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var order = Assert.Single(fixture.Acme.Orders);
        Assert.Equal(PanelName, order.Domain);
        Assert.IsType<PanelChallengeDirectory>(order.Challenge);
        Assert.Single(fixture.Agent.PanelInstalls);
        Assert.Equal(PanelName, (await fixture.DbContext.PanelCertificates.SingleAsync()).HostName);
    }

    /// <summary>Issuing again renews: the recorded row is replaced, not duplicated.</summary>
    [Fact]
    public async Task Issuing_again_renews_the_recorded_row_instead_of_adding_one()
    {
        using var fixture = new SslHandlerFixture([]);
        var issuer = IssuerFor(fixture, new StubPanelHostName(PanelName));
        await issuer.IssueAsync(Operator, CancellationToken.None);

        await issuer.IssueAsync(Operator, CancellationToken.None);

        Assert.Equal(1, await fixture.DbContext.PanelCertificates.CountAsync());
        Assert.Equal(2, fixture.Agent.PanelInstalls.Count);
    }

    /// <summary>The success is journalled with the panel as actor, the console as origin and the operator as a claim.</summary>
    [Fact]
    public async Task The_success_is_journalled_with_the_panel_as_actor_and_the_operator_as_a_claim()
    {
        using var fixture = new SslHandlerFixture([]);

        await IssuerFor(fixture, new StubPanelHostName(PanelName)).IssueAsync(Operator, CancellationToken.None);

        var entry = Assert.Single(fixture.Audit.Entries);
        Assert.Equal(CertificateAuditActions.CertificateIssued, entry.Action);
        Assert.Equal(PanelName, entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.Null(entry.ActorUserId);
        Assert.Equal(SystemAuditEntry.NameFor(CertificateAuditJournal.ModuleName), entry.ActorUsername);
        Assert.Equal(SystemAuditEntry.ConsoleOrigin, entry.IpAddress);
        Assert.Equal(SystemAuditEntry.OperatorClaimPrefix + Operator, entry.UserAgent);
    }

    /// <summary>A panel with no host name is refused before anything is ordered, and the refusal is journalled.</summary>
    [Fact]
    public async Task A_panel_with_no_host_name_is_refused_before_anything_is_ordered()
    {
        using var fixture = new SslHandlerFixture([]);

        var result = await IssuerFor(fixture, new StubPanelHostName()).IssueAsync(Operator, CancellationToken.None);

        Assert.Equal("PanelHostNameNotConfigured", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(fixture.Acme.Orders);
        Assert.False(Assert.Single(fixture.Audit.Entries).Succeeded);
    }

    /// <summary>A refused order installs and records nothing, and the refusal is journalled under the host name.</summary>
    [Fact]
    public async Task A_refused_order_installs_and_records_nothing_and_is_journalled_under_the_host_name()
    {
        using var fixture = new SslHandlerFixture([], acmeFailure: Error.Of("AgentBelowFreeSpaceFloor", ErrorType.Unavailable));

        var result = await IssuerFor(fixture, new StubPanelHostName(PanelName)).IssueAsync(Operator, CancellationToken.None);

        Assert.Equal("AgentBelowFreeSpaceFloor", result.Error!.Code);
        Assert.Empty(fixture.Agent.PanelInstalls);
        Assert.Equal(0, await fixture.DbContext.PanelCertificates.CountAsync());
        var entry = Assert.Single(fixture.Audit.Entries);
        Assert.Equal((PanelName, false), (entry.Subject, entry.Succeeded));
    }

    /// <summary>A claim with control characters is tidied before it reaches the journal.</summary>
    [Fact]
    public async Task A_claim_with_control_characters_is_tidied_before_it_reaches_the_journal()
    {
        using var fixture = new SslHandlerFixture([]);

        await IssuerFor(fixture, new StubPanelHostName(PanelName)).IssueAsync("al\nice", CancellationToken.None);

        Assert.Equal(SystemAuditEntry.OperatorClaimPrefix + "alice", Assert.Single(fixture.Audit.Entries).UserAgent);
    }
}
