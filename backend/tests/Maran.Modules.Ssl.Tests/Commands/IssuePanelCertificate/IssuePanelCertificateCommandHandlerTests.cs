using Maran.Modules.Ssl.Commands.IssuePanelCertificate;
using Maran.Modules.Ssl.Models;
using Maran.Modules.Ssl.Services;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Ssl.Tests.Commands.IssuePanelCertificate;

/// <summary>The panel's own certificate issuance, driven through its handler with the authority faked.</summary>
public sealed class IssuePanelCertificateCommandHandlerTests
{
    /// <summary>The panel host name every test here is configured with.</summary>
    private const string PanelName = "panel.example.net";

    /// <summary>The command every test sends.</summary>
    private static readonly IssuePanelCertificateCommand Command = new("203.0.113.7", "tests");

    /// <summary>Builds the handler over the fixture with the given panel-name window.</summary>
    private static IssuePanelCertificateCommandHandler HandlerFor(SslHandlerFixture fixture, StubPanelHostName name)
    {
        return new IssuePanelCertificateCommandHandler(
            name,
            new PanelCertificateIssuer(fixture.DbContext, fixture.Acme, fixture.Agent, fixture.Clock),
            fixture.Journal,
            fixture.Tasks,
            fixture.CorrelationIds);
    }

    /// <summary>Issuing orders for the panel name with the panel's own challenge and installs the panel's pair.</summary>
    [Fact]
    public async Task Issuing_orders_for_the_panel_name_with_the_panels_challenge_and_installs_the_panels_pair()
    {
        using var fixture = new SslHandlerFixture([]);

        var result = await HandlerFor(fixture, new StubPanelHostName(PanelName)).HandleAsync(Command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PanelName, result.Value.HostName);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), result.Value.ExpiresAt);
        var order = Assert.Single(fixture.Acme.Orders);
        Assert.Equal(PanelName, order.Domain);
        Assert.IsType<PanelChallengeDirectory>(order.Challenge);
        Assert.Single(fixture.Agent.PanelInstalls);
        Assert.Empty(fixture.Agent.Installs);
        var stored = await fixture.DbContext.PanelCertificates.SingleAsync();
        Assert.Equal(SslHandlerFixture.Now, stored.IssuedAt);
        var entry = Assert.Single(fixture.Audit.Entries);
        Assert.Equal((AuditActions.CertificateIssued, PanelName, true), (entry.Action, entry.Subject, entry.Succeeded));
        var task = Assert.Single(fixture.Tasks.Tasks);
        Assert.Equal(TaskAudience.AdministratorOnly, task.Audience);
        Assert.Null(task.AccountId);
        Assert.True(task.Completed);
    }

    /// <summary>Issuing again for the same name replaces the recorded expiry instead of adding a row.</summary>
    [Fact]
    public async Task Issuing_again_for_the_same_name_replaces_the_recorded_expiry_instead_of_adding_a_row()
    {
        using var fixture = new SslHandlerFixture([]);
        var handler = HandlerFor(fixture, new StubPanelHostName(PanelName));
        await handler.HandleAsync(Command, CancellationToken.None);

        await handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(1, await fixture.DbContext.PanelCertificates.CountAsync());
    }

    /// <summary>A panel with no host name is refused before anything is ordered.</summary>
    [Fact]
    public async Task A_panel_with_no_host_name_is_refused_before_anything_is_ordered()
    {
        using var fixture = new SslHandlerFixture([]);

        var result = await HandlerFor(fixture, new StubPanelHostName()).HandleAsync(Command, CancellationToken.None);

        Assert.Equal("PanelHostNameNotConfigured", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(fixture.Acme.Orders);
        Assert.False(Assert.Single(fixture.Audit.Entries).Succeeded);
    }

    /// <summary>A refused order installs nothing and records nothing, so the self-signed pair stays.</summary>
    [Fact]
    public async Task A_refused_order_installs_nothing_and_records_nothing_so_the_self_signed_pair_stays()
    {
        using var fixture = new SslHandlerFixture([], acmeFailure: Error.Of("AgentBelowFreeSpaceFloor", ErrorType.Unavailable));

        var result = await HandlerFor(fixture, new StubPanelHostName(PanelName)).HandleAsync(Command, CancellationToken.None);

        Assert.Equal("AgentBelowFreeSpaceFloor", result.Error!.Code);
        Assert.Empty(fixture.Agent.PanelInstalls);
        Assert.Equal(0, await fixture.DbContext.PanelCertificates.CountAsync());
        Assert.Equal("AgentBelowFreeSpaceFloor", Assert.Single(fixture.Tasks.Tasks).FailureCode);
    }
}
