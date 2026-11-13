using Maran.Modules.Ssl.Commands.RemoveCertificate;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Ssl.Tests.Commands.RemoveCertificate;

/// <summary>
/// The removal handler holds the site's operation lock from before the site is read until the row is
/// saved, so the plain-HTTP vhost it writes is never interleaved with another operation on the site.
/// </summary>
public sealed class RemoveCertificateSiteLockTests
{
    /// <summary>The domain every test here removes a certificate for.</summary>
    private const string Domain = "example.com";

    /// <summary>The longest any waiting step may take before the test calls it a hang.</summary>
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(10);

    /// <summary>Builds the handler over a fixture's doubles.</summary>
    /// <param name="fixture">The fixture supplying the context and doubles.</param>
    /// <returns>The handler under test.</returns>
    private static RemoveCertificateCommandHandler HandlerFor(SslHandlerFixture fixture)
    {
        return new RemoveCertificateCommandHandler(
            fixture.DbContext,
            fixture.Sites,
            fixture.Accounts,
            fixture.Agent,
            fixture.Journal,
            fixture.Stack,
            fixture.SiteLocks);
    }

    /// <summary>Seeds a certificate for the fixture's site and returns its identity.</summary>
    /// <param name="fixture">The fixture to seed.</param>
    /// <returns>The seeded certificate's identity.</returns>
    private static async Task<Guid> SeedAsync(SslHandlerFixture fixture)
    {
        var certificate = SslTestContext.Certificate(
            fixture.AccountId, Domain, SslHandlerFixture.Now.AddDays(60), siteId: fixture.SiteFor(Domain).Id);
        fixture.DbContext.Certificates.Add(certificate);
        await fixture.DbContext.SaveChangesAsync();
        fixture.DbContext.ChangeTracker.Clear();

        return certificate.Id;
    }

    /// <summary>A busy site is refused as busy, journalled, and neither the host nor the row is touched.</summary>
    [Fact]
    public async Task A_busy_site_is_refused_as_busy_and_nothing_is_touched()
    {
        var busy = new BusySiteLock();
        using var fixture = new SslHandlerFixture([Domain], siteLock: busy);
        var id = await SeedAsync(fixture);

        var result = await HandlerFor(fixture).HandleAsync(
            new RemoveCertificateCommand(id, "203.0.113.7", "tests"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteBusy", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(fixture.SiteFor(Domain).Id, Assert.Single(busy.Attempts).SiteId);
        Assert.Empty(fixture.Agent.Removals);
        Assert.Empty(fixture.Sites.Detached);
        Assert.Single(await fixture.DbContext.Certificates.ToListAsync());
        Assert.False(Assert.Single(fixture.Audit.Entries).Succeeded);
    }

    /// <summary>The accepting control: a free site is removed and is free again afterwards.</summary>
    [Fact]
    public async Task A_free_site_is_removed_and_is_free_again_afterwards()
    {
        using var fixture = new SslHandlerFixture([Domain]);
        var id = await SeedAsync(fixture);

        var result = await HandlerFor(fixture).HandleAsync(
            new RemoveCertificateCommand(id, "203.0.113.7", "tests"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(fixture.Agent.Removals);
        Assert.True((await fixture.SiteLocks.AcquireAsync(
            fixture.SiteFor(Domain).Id, TimeSpan.Zero, CancellationToken.None)).IsSuccess);
    }

    /// <summary>A certificate that is not the caller's never reaches the lock.</summary>
    [Fact]
    public async Task A_certificate_that_is_not_found_never_reaches_the_lock()
    {
        var busy = new BusySiteLock();
        using var fixture = new SslHandlerFixture([Domain], siteLock: busy);

        var result = await HandlerFor(fixture).HandleAsync(
            new RemoveCertificateCommand(Guid.NewGuid(), "203.0.113.7", "tests"), CancellationToken.None);

        Assert.Equal("CertificateNotFound", result.Error!.Code);
        Assert.Empty(busy.Attempts);
    }
}
