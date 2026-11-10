using System.Resources;
using Maran.Modules.Ssl.Queries.GetIssuanceOptions;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Ssl.Tests.Queries.GetIssuanceOptions;

/// <summary>
/// What the screen is told it may offer: a wildcard when the panel can prove it, and the panel's own reason
/// when not. A domain the caller does not host answers not found, exactly as an unknown one does.
/// </summary>
public sealed class GetIssuanceOptionsQueryHandlerTests
{
    /// <summary>The domain the tests ask about.</summary>
    private const string Domain = "example.com";

    /// <summary>Builds the handler over a fixture, reading sentences from the module's real English table.</summary>
    private static GetIssuanceOptionsQueryHandler HandlerFor(SslHandlerFixture fixture)
    {
        var errors = new ResourceManager("Maran.Modules.Ssl.Resources.ErrorMessages", typeof(SslModule).Assembly);

        return new GetIssuanceOptionsQueryHandler(
            fixture.Sites,
            fixture.Wildcards,
            new ResxErrorTextProvider([errors], NullLogger<ResxErrorTextProvider>.Instance));
    }

    /// <summary>A served zone on a plan with DNS reports the wildcard as available with no reason.</summary>
    [Fact]
    public async Task A_served_zone_on_a_plan_with_dns_reports_the_wildcard_as_available()
    {
        using var fixture = new SslHandlerFixture([Domain]);

        var result = await HandlerFor(fixture).HandleAsync(new GetIssuanceOptionsQuery(Domain), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.WildcardAvailable);
        Assert.Null(result.Value.WildcardUnavailableCode);
        Assert.Null(result.Value.WildcardUnavailableMessage);
    }

    /// <summary>A zone this server does not serve reports the wildcard as unavailable with the panels own sentence.</summary>
    [Fact]
    public async Task A_zone_this_server_does_not_serve_reports_the_panels_own_sentence()
    {
        using var fixture = new SslHandlerFixture([Domain]);
        fixture.DnsChallenges.Availability = DnsChallengeAvailability.NoZone;

        var result = await HandlerFor(fixture).HandleAsync(new GetIssuanceOptionsQuery(Domain), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.WildcardAvailable);
        Assert.Equal("WildcardZoneNotServed", result.Value.WildcardUnavailableCode);
        Assert.Contains("does not serve the DNS zone", result.Value.WildcardUnavailableMessage, StringComparison.Ordinal);
    }

    /// <summary>A plan without DNS reports that as the reason.</summary>
    [Fact]
    public async Task A_plan_without_dns_reports_that_as_the_reason()
    {
        using var fixture = new SslHandlerFixture([Domain]);
        fixture.Features.Included = AccountFeature.All & ~AccountFeature.Dns;

        var result = await HandlerFor(fixture).HandleAsync(new GetIssuanceOptionsQuery(Domain), CancellationToken.None);

        Assert.Equal("WildcardFeatureNotIncluded", result.Value.WildcardUnavailableCode);
    }

    /// <summary>A domain the caller does not host is not found, and nothing about the zones is asked.</summary>
    [Fact]
    public async Task A_domain_the_caller_does_not_host_is_not_found()
    {
        using var fixture = new SslHandlerFixture([Domain]);

        var result = await HandlerFor(fixture).HandleAsync(new GetIssuanceOptionsQuery("elsewhere.example.org"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteNotFound", result.Error!.Code);
        Assert.Empty(fixture.DnsChallenges.Checked);
    }
}
