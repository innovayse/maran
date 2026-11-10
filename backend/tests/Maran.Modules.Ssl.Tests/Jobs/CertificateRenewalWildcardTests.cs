using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Jobs;
using Maran.Modules.Ssl.Models;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Ssl.Tests.Jobs;

/// <summary>
/// Renewal repeats the way a certificate was ordered: a wildcard is proven again through the account's DNS zone,
/// a plain certificate through the document root, and the journal tells the two apart.
/// </summary>
public sealed class CertificateRenewalWildcardTests
{
    /// <summary>The domain both certificates in these tests were issued for.</summary>
    private const string Domain = "soon.example.com";

    /// <summary>Builds the job over a fixture's doubles.</summary>
    private static CertificateRenewalHandler HandlerFor(SslHandlerFixture fixture)
    {
        return new CertificateRenewalHandler(
            fixture.DbContext,
            fixture.Sites,
            fixture.Accounts,
            fixture.Acme,
            fixture.Installer,
            fixture.AgentSites,
            fixture.Journal,
            fixture.Alerts,
            fixture.Clock,
            NullLogger<CertificateRenewalHandler>.Instance,
            fixture.Tasks);
    }

    /// <summary>Seeds a certificate five days from its end for the fixture's site.</summary>
    private static async Task SeedAsync(SslHandlerFixture fixture, bool isWildcard)
    {
        fixture.DbContext.Certificates.Add(SslTestContext.Certificate(
            fixture.AccountId,
            Domain,
            SslHandlerFixture.Now.AddDays(5),
            CertificateSource.Acme,
            fixture.SiteFor(Domain).Id,
            isWildcard));
        await fixture.DbContext.SaveChangesAsync();
    }

    /// <summary>A wildcard certificate is renewed through the accounts DNS zone, which the row names.</summary>
    [Fact]
    public async Task A_wildcard_certificate_is_renewed_through_the_accounts_dns_zone()
    {
        using var fixture = new SslHandlerFixture([Domain]);
        await SeedAsync(fixture, isWildcard: true);

        var renewed = await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        Assert.Equal(1, renewed);
        var order = Assert.Single(fixture.Acme.Orders);
        Assert.Equal(new DnsZoneChallenge(fixture.AccountId), order.Challenge);
        Assert.True(order.CoversWildcard);
        Assert.True((await fixture.DbContext.Certificates.SingleAsync()).IsWildcard);
    }

    /// <summary>A plain certificate is renewed through the document root, as before.</summary>
    [Fact]
    public async Task A_plain_certificate_is_renewed_through_the_document_root()
    {
        using var fixture = new SslHandlerFixture([Domain]);
        await SeedAsync(fixture, isWildcard: false);

        await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        var order = Assert.Single(fixture.Acme.Orders);
        Assert.Equal(new SiteDocumentRootChallenge(fixture.Username), order.Challenge);
        Assert.False(order.CoversWildcard);
    }

    /// <summary>The journal marks a wildcard renewal, success and failure alike, and not a plain one.</summary>
    [Fact]
    public async Task The_journal_marks_a_wildcard_renewal_and_not_a_plain_one()
    {
        using var wildcard = new SslHandlerFixture([Domain]);
        using var plain = new SslHandlerFixture([Domain]);
        using var failing = new SslHandlerFixture([Domain], acmeFailure: Error.Of("AcmeDnsChallengeFailed", ErrorType.UpstreamRefused));
        await SeedAsync(wildcard, isWildcard: true);
        await SeedAsync(plain, isWildcard: false);
        await SeedAsync(failing, isWildcard: true);

        await HandlerFor(wildcard).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);
        await HandlerFor(plain).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);
        await HandlerFor(failing).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        Assert.Equal(Domain + " (wildcard)", Assert.Single(wildcard.Audit.Entries).Subject);
        Assert.Equal(Domain, Assert.Single(plain.Audit.Entries).Subject);
        var refused = Assert.Single(failing.Audit.Entries);
        Assert.False(refused.Succeeded);
        Assert.Equal(Domain + " (wildcard)", refused.Subject);
        Assert.Equal("AcmeDnsChallengeFailed", (await failing.DbContext.Certificates.SingleAsync()).LastRenewalFailureCode);
    }
}
