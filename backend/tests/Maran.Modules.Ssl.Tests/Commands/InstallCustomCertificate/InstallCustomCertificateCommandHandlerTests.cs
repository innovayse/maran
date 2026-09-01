using Maran.Modules.Ssl.Commands.InstallCustomCertificate;
using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Ssl.Tests.Commands.InstallCustomCertificate;

/// <summary>The customer-supplied install handler, driven through its own entry point.</summary>
public sealed class InstallCustomCertificateCommandHandlerTests
{
    /// <summary>The domain every test in this class installs for.</summary>
    private const string Domain = "example.com";

    /// <summary>A certificate body that looks like one.</summary>
    private const string CertificatePem = "-----BEGIN CERTIFICATE-----\nleaf\n-----END CERTIFICATE-----";

    /// <summary>A key that is unmistakable if it ever escapes.</summary>
    private const string PrivateKeyPem = "-----BEGIN PRIVATE KEY-----\nCANARY\n-----END PRIVATE KEY-----";

    /// <summary>Builds the command every test here dispatches.</summary>
    /// <param name="domain">The domain to install for.</param>
    /// <returns>The command.</returns>
    private static InstallCustomCertificateCommand Command(string domain = Domain)
    {
        return new InstallCustomCertificateCommand(domain, CertificatePem, PrivateKeyPem, "203.0.113.7", "tests");
    }

    /// <summary>Builds the handler over a fixture's doubles.</summary>
    /// <param name="fixture">The fixture supplying the context and doubles.</param>
    /// <returns>The handler under test.</returns>
    private static InstallCustomCertificateCommandHandler HandlerFor(SslHandlerFixture fixture)
    {
        return new InstallCustomCertificateCommandHandler(
            fixture.DbContext, fixture.Sites, fixture.Accounts, fixture.Installer, fixture.Journal, fixture.Clock);
    }

    /// <summary>Installing records the certificate as customer supplied so renewal never touches it.</summary>
    [Fact]
    public async Task Installing_records_the_certificate_as_customer_supplied_so_renewal_never_touches_it()
    {
        using var fixture = new SslHandlerFixture([Domain]);

        var result = await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CertificateSource.Custom, result.Value.Source);
        Assert.Equal(CertificateSource.Custom, (await fixture.DbContext.Certificates.SingleAsync()).Source);
    }

    /// <summary>The expiry stored is the one the agent read off the material not one the caller supplied.</summary>
    [Fact]
    public async Task The_expiry_stored_is_the_one_the_agent_read_off_the_material_not_one_the_caller_supplied()
    {
        using var fixture = new SslHandlerFixture([Domain]);

        var result = await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);

        Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), result.Value.NotAfter);
    }

    /// <summary>The material reaches the agent and nothing else.</summary>
    [Fact]
    public async Task The_material_reaches_the_agent_and_nothing_else()
    {
        using var fixture = new SslHandlerFixture([Domain]);

        await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);

        var install = Assert.Single(fixture.Agent.Installs);
        Assert.Equal(PrivateKeyPem, install.PrivateKeyPem);

        foreach (var entry in fixture.Audit.Entries)
        {
            Assert.DoesNotContain("CANARY", entry.Subject, StringComparison.Ordinal);
        }

        // And nothing about the material is on the row: the entity has no column for it.
        var stored = await fixture.DbContext.Certificates.SingleAsync();
        Assert.Equal(Domain, stored.Domain);
    }

    /// <summary>Installing marks the site as carrying a certificate.</summary>
    [Fact]
    public async Task Installing_marks_the_site_as_carrying_a_certificate()
    {
        using var fixture = new SslHandlerFixture([Domain]);

        await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);

        Assert.Equal([fixture.SiteFor(Domain).Id], fixture.Sites.Attached);
    }

    /// <summary>Installing over an existing certificate replaces it rather than being refused.</summary>
    [Fact]
    public async Task Installing_over_an_existing_certificate_replaces_it_rather_than_being_refused()
    {
        using var fixture = new SslHandlerFixture([Domain]);
        fixture.DbContext.Certificates.Add(SslTestContext.Certificate(
            fixture.AccountId, Domain, SslHandlerFixture.Now.AddDays(3)));
        await fixture.DbContext.SaveChangesAsync();
        fixture.DbContext.ChangeTracker.Clear();

        var result = await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = Assert.Single(await fixture.DbContext.Certificates.ToListAsync());
        Assert.Equal(CertificateSource.Custom, stored.Source);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), stored.NotAfter);
    }

    /// <summary>A domain the caller does not own is not found rather than forbidden.</summary>
    [Fact]
    public async Task A_domain_the_caller_does_not_own_is_not_found_rather_than_forbidden()
    {
        using var fixture = new SslHandlerFixture([Domain]);

        var result = await HandlerFor(fixture).HandleAsync(
            Command("someone-else.example.com"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteNotFound", result.Error!.Code);
        Assert.Empty(fixture.Agent.Installs);
    }

    /// <summary>An agent that refuses the material leaves no row and no flag.</summary>
    [Fact]
    public async Task An_agent_that_refuses_the_material_leaves_no_row_and_no_flag()
    {
        using var fixture = new SslHandlerFixture([Domain], agentFailure: Error.Of("AgentValidationFailed"));

        var result = await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(await fixture.DbContext.Certificates.ToListAsync());
        Assert.Empty(fixture.Sites.Attached);
    }
}
