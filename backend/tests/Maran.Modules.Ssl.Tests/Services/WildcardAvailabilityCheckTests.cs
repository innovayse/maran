using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Services;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Ssl.Tests.Services;

/// <summary>The one answer to "can a wildcard be ordered here": the plan first, then the DNS module, then the zone.</summary>
public sealed class WildcardAvailabilityCheckTests
{
    /// <summary>The account asked about.</summary>
    private static readonly Guid AccountId = Guid.NewGuid();

    /// <summary>A plan that sells DNS, a DNS module and a served zone make a wildcard available.</summary>
    [Fact]
    public async Task A_plan_with_dns_a_dns_module_and_a_served_zone_make_a_wildcard_available()
    {
        var check = new WildcardAvailabilityCheck(new StubAccountFeatureDirectory(), new AcmeDnsChallengeWriter([new RecordingDnsChallengeRecords()]));

        Assert.Equal(WildcardAvailability.Available, await check.CheckAsync(AccountId, "example.com", CancellationToken.None));
    }

    /// <summary>The plan is asked first: without DNS the zone is never asked about.</summary>
    [Fact]
    public async Task The_plan_is_asked_first_and_the_zone_is_not_asked_without_it()
    {
        var features = new StubAccountFeatureDirectory { Included = AccountFeature.All & ~AccountFeature.Dns };
        var records = new RecordingDnsChallengeRecords();
        var check = new WildcardAvailabilityCheck(features, new AcmeDnsChallengeWriter([records]));

        var availability = await check.CheckAsync(AccountId, "example.com", CancellationToken.None);

        Assert.Equal(WildcardAvailability.FeatureNotIncluded, availability);
        Assert.Equal([AccountId], features.AccountsAsked);
        Assert.Empty(records.Checked);
    }

    /// <summary>A plan that sells DNS but a panel with no DNS module reports the module as the reason.</summary>
    [Fact]
    public async Task A_panel_with_no_dns_module_reports_the_module_as_the_reason()
    {
        var check = new WildcardAvailabilityCheck(new StubAccountFeatureDirectory(), new AcmeDnsChallengeWriter([]));

        Assert.Equal(WildcardAvailability.DnsModuleAbsent, await check.CheckAsync(AccountId, "example.com", CancellationToken.None));
    }
}
