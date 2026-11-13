using Maran.Modules.Ssl.Commands.InstallCustomCertificate;
using Maran.Modules.Ssl.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Ssl.Tests.Commands.InstallCustomCertificate;

/// <summary>
/// A customer-supplied install goes through the installer, so it is serialised with every other
/// operation on the site and answers a site that stays busy as such, writing no row.
/// </summary>
public sealed class InstallCustomCertificateSiteLockTests
{
    /// <summary>The domain every test here installs for.</summary>
    private const string Domain = "example.com";

    /// <summary>Builds the handler over a fixture's doubles.</summary>
    /// <param name="fixture">The fixture supplying the context and doubles.</param>
    /// <returns>The handler under test.</returns>
    private static InstallCustomCertificateCommandHandler HandlerFor(SslHandlerFixture fixture)
    {
        return new InstallCustomCertificateCommandHandler(
            fixture.DbContext, fixture.Sites, fixture.Accounts, fixture.Installer, fixture.Journal, fixture.Clock);
    }

    /// <summary>Builds the command every test here dispatches.</summary>
    /// <returns>The command.</returns>
    private static InstallCustomCertificateCommand Command()
    {
        return new InstallCustomCertificateCommand(
            Domain,
            "-----BEGIN CERTIFICATE-----\nleaf\n-----END CERTIFICATE-----",
            "-----BEGIN PRIVATE KEY-----\nkey\n-----END PRIVATE KEY-----",
            "203.0.113.7",
            "tests");
    }

    /// <summary>A busy site is refused as busy, journalled, and no certificate row is written.</summary>
    [Fact]
    public async Task A_busy_site_is_refused_as_busy_and_no_row_is_written()
    {
        var busy = new BusySiteLock();
        using var fixture = new SslHandlerFixture([Domain], siteLock: busy);

        var result = await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteBusy", result.Error!.Code);
        Assert.Equal(fixture.SiteFor(Domain).Id, Assert.Single(busy.Attempts).SiteId);
        Assert.Empty(fixture.Agent.Installs);
        Assert.Empty(await fixture.DbContext.Certificates.ToListAsync());
        Assert.False(Assert.Single(fixture.Audit.Entries).Succeeded);
    }

    /// <summary>The accepting control: with the site free the same command installs.</summary>
    [Fact]
    public async Task With_the_site_free_the_same_command_installs()
    {
        using var fixture = new SslHandlerFixture([Domain]);

        var result = await HandlerFor(fixture).HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(fixture.Agent.Installs);
        Assert.Single(await fixture.DbContext.Certificates.ToListAsync());
    }
}
