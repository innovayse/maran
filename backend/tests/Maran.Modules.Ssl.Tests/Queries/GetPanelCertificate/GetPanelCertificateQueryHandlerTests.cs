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

        var result = await new GetPanelCertificateQueryHandler(fixture.DbContext, new StubPanelHostName(PanelName))
            .HandleAsync(new GetPanelCertificateQuery(), CancellationToken.None);

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

        var result = await new GetPanelCertificateQueryHandler(fixture.DbContext, new StubPanelHostName(PanelName))
            .HandleAsync(new GetPanelCertificateQuery(), CancellationToken.None);

        Assert.Equal("PanelCertificateNotIssued", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    /// <summary>A panel with no host name answers that, not that nothing was issued.</summary>
    [Fact]
    public async Task A_panel_with_no_host_name_answers_that_not_that_nothing_was_issued()
    {
        using var fixture = new SslHandlerFixture([]);

        var result = await new GetPanelCertificateQueryHandler(fixture.DbContext, new StubPanelHostName())
            .HandleAsync(new GetPanelCertificateQuery(), CancellationToken.None);

        Assert.Equal("PanelHostNameNotConfigured", result.Error!.Code);
    }
}
