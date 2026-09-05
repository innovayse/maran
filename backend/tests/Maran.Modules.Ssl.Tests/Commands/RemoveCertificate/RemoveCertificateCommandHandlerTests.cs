using Maran.Modules.Ssl.Commands.RemoveCertificate;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Ssl.Tests.Commands.RemoveCertificate;

/// <summary>The removal handler, driven through its own entry point.</summary>
public sealed class RemoveCertificateCommandHandlerTests
{
    /// <summary>The domain every test in this class removes a certificate for.</summary>
    private const string Domain = "example.com";

    /// <summary>Builds the handler over a fixture's doubles.</summary>
    /// <param name="fixture">The fixture supplying the context and doubles.</param>
    /// <returns>The handler under test.</returns>
    private static RemoveCertificateCommandHandler HandlerFor(SslHandlerFixture fixture)
    {
        return new RemoveCertificateCommandHandler(
            fixture.DbContext, fixture.Sites, fixture.Accounts, fixture.Agent, fixture.Journal);
    }

    /// <summary>Seeds a certificate for the fixture's own account and returns its identity.</summary>
    /// <param name="fixture">The fixture to seed.</param>
    /// <param name="accountId">The owning account; the fixture's own when omitted.</param>
    /// <returns>The seeded certificate's identity.</returns>
    private static async Task<Guid> SeedAsync(SslHandlerFixture fixture, Guid? accountId = null)
    {
        var certificate = SslTestContext.Certificate(
            accountId ?? fixture.AccountId,
            Domain,
            SslHandlerFixture.Now.AddDays(60),
            siteId: fixture.SiteFor(Domain).Id);
        fixture.DbContext.Certificates.Add(certificate);
        await fixture.DbContext.SaveChangesAsync();
        fixture.DbContext.ChangeTracker.Clear();
        return certificate.Id;
    }

    /// <summary>Removing takes the material off the host and deletes the row.</summary>
    [Fact]
    public async Task Removing_takes_the_material_off_the_host_and_deletes_the_row()
    {
        using var fixture = new SslHandlerFixture([Domain]);
        var id = await SeedAsync(fixture);

        var result = await HandlerFor(fixture).HandleAsync(
            new RemoveCertificateCommand(id, "203.0.113.7", "tests"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(fixture.Agent.Removals);
        Assert.Empty(await fixture.DbContext.Certificates.ToListAsync());
    }

    /// <summary>Removing clears the sites certificate flag so the next render does not write tls.</summary>
    [Fact]
    public async Task Removing_clears_the_sites_certificate_flag_so_the_next_render_does_not_write_tls()
    {
        using var fixture = new SslHandlerFixture([Domain]);
        var id = await SeedAsync(fixture);

        await HandlerFor(fixture).HandleAsync(
            new RemoveCertificateCommand(id, "203.0.113.7", "tests"), CancellationToken.None);

        Assert.Equal([fixture.SiteFor(Domain).Id], fixture.Sites.Detached);

        // And the vhost the agent was asked to write is a plain-HTTP one, not a TLS one.
        Assert.False(Assert.Single(fixture.Agent.Removals).Site.HasCertificate);
    }

    /// <summary>Another customers certificate answers not found rather than forbidden.</summary>
    [Fact]
    public async Task Another_customers_certificate_answers_not_found_rather_than_forbidden()
    {
        using var fixture = new SslHandlerFixture([Domain]);
        var id = await SeedAsync(fixture, accountId: Guid.NewGuid());

        var result = await HandlerFor(fixture).HandleAsync(
            new RemoveCertificateCommand(id, "203.0.113.7", "tests"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("CertificateNotFound", result.Error!.Code);
        Assert.Empty(fixture.Agent.Removals);
        Assert.Empty(fixture.Sites.Detached);
    }

    /// <summary>An agent that refuses keeps the row so the certificate can still be renewed.</summary>
    [Fact]
    public async Task An_agent_that_refuses_keeps_the_row_so_the_certificate_can_still_be_renewed()
    {
        using var fixture = new SslHandlerFixture([Domain], agentFailure: Error.Of("AgentSystemFailure", ErrorType.Failure));
        var id = await SeedAsync(fixture);

        var result = await HandlerFor(fixture).HandleAsync(
            new RemoveCertificateCommand(id, "203.0.113.7", "tests"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Single(await fixture.DbContext.Certificates.ToListAsync());
        Assert.Empty(fixture.Sites.Detached);
    }

    /// <summary>A refusal is journalled naming what was probed for.</summary>
    [Fact]
    public async Task A_refusal_is_journalled_naming_what_was_probed_for()
    {
        using var fixture = new SslHandlerFixture([Domain]);
        var missing = Guid.NewGuid();

        await HandlerFor(fixture).HandleAsync(
            new RemoveCertificateCommand(missing, "203.0.113.7", "tests"), CancellationToken.None);

        var entry = Assert.Single(fixture.Audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal(missing.ToString(), entry.Subject);
    }

    /// <summary>Removal clears the flag on the site the certificate row names.</summary>
    [Fact]
    public async Task Removal_clears_the_flag_on_the_site_the_certificate_row_names()
    {
        // The ROW's own linkage, not whichever site a domain lookup returned. The two agree in
        // production because Site.Domain is unique — which is precisely why a test that seeds them
        // equal cannot tell the two apart, and why this one seeds them DIFFERENT.
        using var fixture = new SslHandlerFixture([Domain]);
        var recordedSiteId = Guid.NewGuid();
        Assert.NotEqual(fixture.SiteFor(Domain).Id, recordedSiteId);
        var recorded = SslTestContext.Certificate(
            fixture.AccountId, Domain, SslHandlerFixture.Now.AddDays(60), siteId: recordedSiteId);
        fixture.DbContext.Certificates.Add(recorded);
        await fixture.DbContext.SaveChangesAsync();
        fixture.DbContext.ChangeTracker.Clear();

        await HandlerFor(fixture).HandleAsync(
            new RemoveCertificateCommand(recorded.Id, "203.0.113.7", "tests"), CancellationToken.None);

        Assert.Equal([recorded.SiteId], fixture.Sites.Detached);
    }
}
