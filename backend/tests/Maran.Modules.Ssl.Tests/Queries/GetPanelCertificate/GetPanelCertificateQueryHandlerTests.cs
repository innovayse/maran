using Maran.Modules.Ssl.Domain.Entities;
using Maran.Modules.Ssl.Queries.GetPanelCertificate;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Ssl.Tests.Queries.GetPanelCertificate;

/// <summary>What the panel reports about its own certificate.</summary>
public sealed class GetPanelCertificateQueryHandlerTests
{
    /// <summary>The panel host name every test here is configured with.</summary>
    private const string PanelName = "panel.example.net";

    /// <summary>The recorded certificate for the configured name is answered.</summary>
    [Fact]
    public async Task The_recorded_certificate_for_the_configured_name_is_answered()
    {
        using var fixture = new SslHandlerFixture([]);
        var expires = SslHandlerFixture.Now.AddDays(80);
        fixture.DbContext.PanelCertificates.Add(new PanelCertificate(PanelName, expires, SslHandlerFixture.Now));
        await fixture.DbContext.SaveChangesAsync();

        var result = await HandlerFor(fixture, new StubPanelHostName(PanelName))
            .HandleAsync(new GetPanelCertificateQuery(), CancellationToken.None);

        Assert.True(result.Value.HostNameConfigured);
        Assert.Null(result.Value.HostNameNotice);
        Assert.Equal(PanelName, result.Value.HostName);
        Assert.Equal(expires, result.Value.ExpiresAt);
    }

    /// <summary>A certificate recorded for a name the panel no longer has reads as not issued.</summary>
    [Fact]
    public async Task A_certificate_recorded_for_a_name_the_panel_no_longer_has_reads_as_not_issued()
    {
        using var fixture = new SslHandlerFixture([]);
        fixture.DbContext.PanelCertificates.Add(
            new PanelCertificate("old.example.net", SslHandlerFixture.Now.AddDays(80), SslHandlerFixture.Now));
        await fixture.DbContext.SaveChangesAsync();

        var result = await HandlerFor(fixture, new StubPanelHostName(PanelName))
            .HandleAsync(new GetPanelCertificateQuery(), CancellationToken.None);

        Assert.Equal("PanelCertificateNotIssued", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    /// <summary>A panel with no host name is answered as a status flagged so, not as a refusal.</summary>
    [Fact]
    public async Task A_panel_with_no_host_name_is_answered_as_a_flagged_status_not_a_refusal()
    {
        using var fixture = new SslHandlerFixture([]);

        var result = await HandlerFor(fixture, new StubPanelHostName())
            .HandleAsync(new GetPanelCertificateQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HostNameConfigured);
        Assert.Equal(
            StubErrorTextProvider.SentencePrefix + "PanelHostNameNotConfigured",
            result.Value.HostNameNotice);
        Assert.Null(result.Value.HostName);
        Assert.Null(result.Value.ExpiresAt);
        Assert.Null(result.Value.IssuedAt);
    }

    /// <summary>A row recorded for another name does not leak into a panel that now has no host name.</summary>
    [Fact]
    public async Task A_row_recorded_earlier_is_not_reported_for_a_panel_that_now_has_no_host_name()
    {
        using var fixture = new SslHandlerFixture([]);
        fixture.DbContext.PanelCertificates.Add(
            new PanelCertificate(PanelName, SslHandlerFixture.Now.AddDays(80), SslHandlerFixture.Now));
        await fixture.DbContext.SaveChangesAsync();

        var result = await HandlerFor(fixture, new StubPanelHostName())
            .HandleAsync(new GetPanelCertificateQuery(), CancellationToken.None);

        Assert.False(result.Value.HostNameConfigured);
        Assert.Null(result.Value.HostName);
    }

    /// <summary>Builds the handler over the fixture with the given panel-name window.</summary>
    /// <param name="fixture">The fixture whose database is read.</param>
    /// <param name="name">The panel-name window.</param>
    /// <returns>The handler under test.</returns>
    private static GetPanelCertificateQueryHandler HandlerFor(SslHandlerFixture fixture, StubPanelHostName name)
    {
        return new GetPanelCertificateQueryHandler(fixture.DbContext, name, new StubErrorTextProvider());
    }
}
