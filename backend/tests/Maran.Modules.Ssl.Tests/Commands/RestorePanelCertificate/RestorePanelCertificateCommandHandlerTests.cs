using Maran.Modules.Ssl.Commands.RestorePanelCertificate;
using Maran.Modules.Ssl.Domain.Entities;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Ssl.Tests.Commands.RestorePanelCertificate;

/// <summary>Returning the panel to a self-signed certificate, driven through its handler.</summary>
public sealed class RestorePanelCertificateCommandHandlerTests
{
    /// <summary>The panel host name every test here is configured with.</summary>
    private const string PanelName = "panel.example.net";

    /// <summary>The command every test sends.</summary>
    private static readonly RestorePanelCertificateCommand Command = new("203.0.113.7", "tests");

    /// <summary>Builds the handler over the fixture with the given panel-name window.</summary>
    private static RestorePanelCertificateCommandHandler HandlerFor(SslHandlerFixture fixture, StubPanelHostName name)
    {
        return new RestorePanelCertificateCommandHandler(fixture.DbContext, name, fixture.Agent, fixture.Journal);
    }

    /// <summary>Seeds the trusted certificate recorded for a name.</summary>
    private static async Task SeedRecordAsync(SslHandlerFixture fixture, string hostName)
    {
        fixture.DbContext.PanelCertificates.Add(
            new PanelCertificate(hostName, SslHandlerFixture.Now.AddDays(60), SslHandlerFixture.Now));
        await fixture.DbContext.SaveChangesAsync();
    }

    /// <summary>Restoring asks the agent for the configured name, forgets the row and journals the restore.</summary>
    [Fact]
    public async Task Restoring_asks_the_agent_for_the_configured_name_forgets_the_row_and_journals_the_restore()
    {
        using var fixture = new SslHandlerFixture([]);
        await SeedRecordAsync(fixture, PanelName);

        var result = await HandlerFor(fixture, new StubPanelHostName(PanelName)).HandleAsync(Command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PanelName, result.Value.HostName);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), result.Value.ExpiresAt);
        Assert.Equal(PanelName, Assert.Single(fixture.Agent.PanelSelfSignedRestores));
        Assert.Empty(fixture.Agent.PanelInstalls);
        Assert.Equal(0, await fixture.DbContext.PanelCertificates.CountAsync());
        var entry = Assert.Single(fixture.Audit.Entries);
        Assert.Equal(
            (CertificateAuditActions.CertificateSelfSignedRestored, PanelName, true),
            (entry.Action, entry.Subject, entry.Succeeded));
    }

    /// <summary>Restoring a panel that has no recorded certificate succeeds and changes no row.</summary>
    [Fact]
    public async Task Restoring_a_panel_with_no_recorded_certificate_succeeds_and_regenerates_the_placeholder()
    {
        using var fixture = new SslHandlerFixture([]);

        var result = await HandlerFor(fixture, new StubPanelHostName(PanelName)).HandleAsync(Command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(fixture.Agent.PanelSelfSignedRestores);
        Assert.Equal(0, await fixture.DbContext.PanelCertificates.CountAsync());
    }

    /// <summary>Only the row of the configured name is forgotten; a row for an older name stays.</summary>
    [Fact]
    public async Task Only_the_row_of_the_configured_name_is_forgotten()
    {
        using var fixture = new SslHandlerFixture([]);
        await SeedRecordAsync(fixture, PanelName);
        await SeedRecordAsync(fixture, "old.example.net");

        await HandlerFor(fixture, new StubPanelHostName(PanelName)).HandleAsync(Command, CancellationToken.None);

        var remaining = await fixture.DbContext.PanelCertificates.SingleAsync();
        Assert.Equal("old.example.net", remaining.HostName);
    }

    /// <summary>A refusal by the agent keeps the row and journals a failure, so nothing claims a restore.</summary>
    [Fact]
    public async Task A_refusal_by_the_agent_keeps_the_row_and_journals_a_failure()
    {
        using var fixture = new SslHandlerFixture(
            [],
            agentFailure: Error.Of("AgentValidationFailed", ErrorType.Failure));
        await SeedRecordAsync(fixture, PanelName);

        var result = await HandlerFor(fixture, new StubPanelHostName(PanelName)).HandleAsync(Command, CancellationToken.None);

        Assert.Equal("AgentValidationFailed", result.Error!.Code);
        Assert.Equal(1, await fixture.DbContext.PanelCertificates.CountAsync());
        var entry = Assert.Single(fixture.Audit.Entries);
        Assert.Equal((CertificateAuditActions.CertificateSelfSignedRestored, PanelName, false),
            (entry.Action, entry.Subject, entry.Succeeded));
    }

    /// <summary>A panel with no host name is refused before the agent is asked.</summary>
    [Fact]
    public async Task A_panel_with_no_host_name_is_refused_before_the_agent_is_asked()
    {
        using var fixture = new SslHandlerFixture([]);

        var result = await HandlerFor(fixture, new StubPanelHostName()).HandleAsync(Command, CancellationToken.None);

        Assert.Equal("PanelHostNameNotConfigured", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(fixture.Agent.PanelSelfSignedRestores);
        Assert.False(Assert.Single(fixture.Audit.Entries).Succeeded);
    }
}
