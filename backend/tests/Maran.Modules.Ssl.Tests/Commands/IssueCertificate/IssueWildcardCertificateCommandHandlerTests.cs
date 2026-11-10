using Maran.Modules.Ssl.Commands.IssueCertificate;
using Maran.Modules.Ssl.Models;
using Maran.Modules.Ssl.Services;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Ssl.Tests.Commands.IssueCertificate;

/// <summary>
/// The issuance handler asked for a wildcard: it needs the plan, the DNS module and a served zone, checks all
/// three before anything is ordered, proves the name over DNS, and keeps the choice on the row for renewal. Each
/// refusal sits beside the plain request that proves the handler still says yes.
/// </summary>
public sealed class IssueWildcardCertificateCommandHandlerTests
{
    /// <summary>The domain every test in this class issues for.</summary>
    private const string Domain = "example.com";

    /// <summary>Builds a wildcard or a plain command for the domain.</summary>
    private static IssueCertificateCommand Command(bool wildcard, string domain = Domain)
    {
        return new IssueCertificateCommand(domain, "203.0.113.7", "tests", wildcard);
    }

    /// <summary>Builds the handler over a fixture, with the wildcard check the fixture composed unless another is given.</summary>
    private static IssueCertificateCommandHandler HandlerFor(SslHandlerFixture fixture, WildcardAvailabilityCheck? wildcards = null)
    {
        return new IssueCertificateCommandHandler(
            fixture.DbContext,
            fixture.Sites,
            fixture.Accounts,
            fixture.Acme,
            fixture.Installer,
            fixture.Journal,
            wildcards ?? fixture.Wildcards,
            fixture.Clock,
            fixture.Tasks,
            fixture.CorrelationIds,
            fixture.CurrentUser);
    }

    /// <summary>A wildcard request is proven over DNS for the sites owner, and the row and the answer say wildcard.</summary>
    [Fact]
    public async Task A_wildcard_request_is_proven_over_dns_for_the_sites_owner_and_the_row_says_wildcard()
    {
        using var fixture = new SslHandlerFixture([Domain]);

        var result = await HandlerFor(fixture).HandleAsync(Command(wildcard: true), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Error!.Code);
        Assert.True(result.Value.Wildcard);
        var order = Assert.Single(fixture.Acme.Orders);
        Assert.Equal(Domain, order.Domain);
        Assert.Equal(new DnsZoneChallenge(fixture.AccountId), order.Challenge);
        Assert.True(order.CoversWildcard);
        Assert.True((await fixture.DbContext.Certificates.SingleAsync()).IsWildcard);
        Assert.Equal([fixture.SiteFor(Domain).Id], fixture.Sites.Attached);
    }

    /// <summary>The audit entry for a wildcard names the domain and marks it, and a plain one names the domain alone.</summary>
    [Fact]
    public async Task The_audit_entry_marks_a_wildcard_and_a_plain_request_is_not_marked()
    {
        using var wildcard = new SslHandlerFixture([Domain]);
        using var plain = new SslHandlerFixture([Domain]);

        await HandlerFor(wildcard).HandleAsync(Command(wildcard: true), CancellationToken.None);
        await HandlerFor(plain).HandleAsync(Command(wildcard: false), CancellationToken.None);

        var marked = Assert.Single(wildcard.Audit.Entries);
        Assert.True(marked.Succeeded);
        Assert.Equal(Domain + " (wildcard)", marked.Subject);
        Assert.Equal(Domain, Assert.Single(plain.Audit.Entries).Subject);
    }

    /// <summary>A plain request stays on the document root, never asks the DNS door and stores a plain certificate.</summary>
    [Fact]
    public async Task A_plain_request_stays_on_the_document_root_and_never_asks_the_dns_door()
    {
        using var fixture = new SslHandlerFixture([Domain]);

        var result = await HandlerFor(fixture).HandleAsync(Command(wildcard: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Wildcard);
        Assert.Equal(new SiteDocumentRootChallenge(fixture.Username), Assert.Single(fixture.Acme.Orders).Challenge);
        Assert.False(Assert.Single(fixture.Acme.Orders).CoversWildcard);
        Assert.Empty(fixture.DnsChallenges.Checked);
        Assert.Empty(fixture.Features.AccountsAsked);
        Assert.False((await fixture.DbContext.Certificates.SingleAsync()).IsWildcard);
    }

    /// <summary>The plan and the zone are asked about the owner of the site.</summary>
    [Fact]
    public async Task The_plan_and_the_zone_are_asked_about_the_owner_of_the_site()
    {
        using var fixture = new SslHandlerFixture([Domain]);

        await HandlerFor(fixture).HandleAsync(Command(wildcard: true), CancellationToken.None);

        Assert.Equal([fixture.SiteFor(Domain).AccountId], fixture.Features.AccountsAsked);
        Assert.Equal([(fixture.SiteFor(Domain).AccountId, Domain)], fixture.DnsChallenges.Checked);
    }

    /// <summary>A plan without DNS refuses the wildcard before anything is ordered, and a plain request still succeeds.</summary>
    [Fact]
    public async Task A_plan_without_dns_refuses_the_wildcard_before_anything_is_ordered()
    {
        using var fixture = new SslHandlerFixture([Domain]);
        fixture.Features.Included = AccountFeature.All & ~AccountFeature.Dns;

        var refused = await HandlerFor(fixture).HandleAsync(Command(wildcard: true), CancellationToken.None);

        Assert.Equal("WildcardFeatureNotIncluded", refused.Error!.Code);
        Assert.Equal(ErrorType.Conflict, refused.Error.Type);
        Assert.Empty(fixture.Acme.Orders);
        Assert.Empty(await fixture.DbContext.Certificates.ToListAsync());

        var plain = await HandlerFor(fixture).HandleAsync(Command(wildcard: false), CancellationToken.None);
        Assert.True(plain.IsSuccess);
    }

    /// <summary>A panel without the DNS module refuses the wildcard with a typed code, not an exception.</summary>
    [Fact]
    public async Task A_panel_without_the_dns_module_refuses_the_wildcard_with_a_typed_code()
    {
        using var fixture = new SslHandlerFixture([Domain]);
        var withoutDns = new WildcardAvailabilityCheck(fixture.Features, new AcmeDnsChallengeWriter([]));

        var refused = await HandlerFor(fixture, withoutDns).HandleAsync(Command(wildcard: true), CancellationToken.None);

        Assert.Equal("WildcardDnsNotAvailable", refused.Error!.Code);
        Assert.Empty(fixture.Acme.Orders);
    }

    /// <summary>A zone this server does not serve, and a name server that is off, each refuse with their own code.</summary>
    [Theory]
    [InlineData(DnsChallengeAvailability.NoZone, "WildcardZoneNotServed")]
    [InlineData(DnsChallengeAvailability.DnsNotEnabled, "WildcardDnsNotEnabled")]
    public async Task A_zone_that_cannot_answer_refuses_the_wildcard_with_its_own_code(
        DnsChallengeAvailability availability,
        string code)
    {
        using var fixture = new SslHandlerFixture([Domain]);
        fixture.DnsChallenges.Availability = availability;

        var refused = await HandlerFor(fixture).HandleAsync(Command(wildcard: true), CancellationToken.None);

        Assert.Equal(code, refused.Error!.Code);
        Assert.Empty(fixture.Acme.Orders);
        Assert.Empty(await fixture.DbContext.Certificates.ToListAsync());

        // The refusal is journalled under the wildcard subject and closes the task under the same code.
        var entry = Assert.Single(fixture.Audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal(Domain + " (wildcard)", entry.Subject);
        Assert.Equal(code, Assert.Single(fixture.Tasks.Tasks).FailureCode);
    }

    /// <summary>A wildcard for a domain the caller does not host is not found, and the DNS door is never asked.</summary>
    [Fact]
    public async Task A_wildcard_for_a_domain_the_caller_does_not_host_is_not_found()
    {
        using var fixture = new SslHandlerFixture([Domain]);

        var refused = await HandlerFor(fixture).HandleAsync(
            Command(wildcard: true, domain: "someone-else.example.org"), CancellationToken.None);

        Assert.Equal("SiteNotFound", refused.Error!.Code);
        Assert.Empty(fixture.DnsChallenges.Checked);
        Assert.Empty(fixture.Acme.Orders);
    }

    /// <summary>An authority failure on a wildcard order leaves no row, like a plain one.</summary>
    [Fact]
    public async Task An_authority_failure_on_a_wildcard_order_leaves_no_row()
    {
        using var fixture = new SslHandlerFixture(
            [Domain], acmeFailure: Error.Of("AcmeDnsChallengeFailed", ErrorType.UpstreamRefused));

        var result = await HandlerFor(fixture).HandleAsync(Command(wildcard: true), CancellationToken.None);

        Assert.Equal("AcmeDnsChallengeFailed", result.Error!.Code);
        Assert.Empty(await fixture.DbContext.Certificates.ToListAsync());
        Assert.Empty(fixture.Agent.Installs);
    }
}
