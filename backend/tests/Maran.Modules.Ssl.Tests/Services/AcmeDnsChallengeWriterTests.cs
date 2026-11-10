using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Services;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Ssl.Tests.Services;

/// <summary>
/// The hook between the certificate module and the DNS module's door: absence of the module is an answer, the
/// door's refusals become this module's codes, and an unreachable agent keeps its own kind.
/// </summary>
public sealed class AcmeDnsChallengeWriterTests
{
    /// <summary>The account asking.</summary>
    private static readonly Guid AccountId = Guid.NewGuid();

    /// <summary>A digest-shaped value.</summary>
    private static readonly string Value = new('a', 43);

    /// <summary>Without a DNS module the check says so and a publish is refused with a typed code, not thrown.</summary>
    [Fact]
    public async Task Without_a_dns_module_the_check_says_so_and_a_publish_is_refused_with_a_typed_code()
    {
        var writer = new AcmeDnsChallengeWriter([]);

        var availability = await writer.CheckAsync(AccountId, "example.com", CancellationToken.None);
        var published = await writer.PublishAsync(AccountId, "example.com", Value, CancellationToken.None);
        var removed = await writer.RemoveAsync(new DnsChallengeHandle(AccountId, Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(WildcardAvailability.DnsModuleAbsent, availability);
        Assert.Equal("WildcardDnsNotAvailable", published.Error!.Code);
        Assert.Equal(ErrorType.Conflict, published.Error.Type);
        Assert.True(removed.IsSuccess);
    }

    /// <summary>The door's three answers become the module's three availabilities.</summary>
    [Theory]
    [InlineData(DnsChallengeAvailability.Available, WildcardAvailability.Available)]
    [InlineData(DnsChallengeAvailability.DnsNotEnabled, WildcardAvailability.DnsNotEnabled)]
    [InlineData(DnsChallengeAvailability.NoZone, WildcardAvailability.ZoneNotServed)]
    public async Task The_doors_availability_becomes_the_modules(DnsChallengeAvailability door, WildcardAvailability expected)
    {
        var records = new RecordingDnsChallengeRecords { Availability = door };

        var availability = await new AcmeDnsChallengeWriter([records]).CheckAsync(AccountId, "example.com", CancellationToken.None);

        Assert.Equal(expected, availability);
        Assert.Equal([(AccountId, "example.com")], records.Checked);
    }

    /// <summary>The door's refusals are worded as this modules codes.</summary>
    [Theory]
    [InlineData("DnsChallengeZoneNotFound", ErrorType.NotFound, "WildcardZoneNotServed", ErrorType.Conflict)]
    [InlineData("ZoneNotFound", ErrorType.NotFound, "WildcardZoneNotServed", ErrorType.Conflict)]
    [InlineData("DnsNotEnabled", ErrorType.Conflict, "WildcardDnsNotEnabled", ErrorType.Conflict)]
    [InlineData("DnsSettingsIncomplete", ErrorType.Conflict, "WildcardDnsNotEnabled", ErrorType.Conflict)]
    [InlineData("DnsChallengeValuesFull", ErrorType.Conflict, "AcmeDnsChallengeWriteFailed", ErrorType.Failure)]
    [InlineData("AgentSystemFailure", ErrorType.Failure, "AcmeDnsChallengeWriteFailed", ErrorType.Failure)]
    public async Task The_doors_refusals_are_worded_as_this_modules_codes(
        string doorCode,
        ErrorType doorType,
        string expectedCode,
        ErrorType expectedType)
    {
        var records = new RecordingDnsChallengeRecords { PublishFailure = Error.Of(doorCode, doorType) };

        var published = await new AcmeDnsChallengeWriter([records]).PublishAsync(AccountId, "example.com", Value, CancellationToken.None);

        Assert.Equal(expectedCode, published.Error!.Code);
        Assert.Equal(expectedType, published.Error.Type);
    }

    /// <summary>An agent that cannot be reached keeps its own code and kind, so the answer stays a 503.</summary>
    [Fact]
    public async Task An_agent_that_cannot_be_reached_keeps_its_own_code_and_kind()
    {
        var records = new RecordingDnsChallengeRecords { PublishFailure = Error.Of("AgentUnavailable", ErrorType.Unavailable) };

        var published = await new AcmeDnsChallengeWriter([records]).PublishAsync(AccountId, "example.com", Value, CancellationToken.None);

        Assert.Equal("AgentUnavailable", published.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, published.Error.Type);
    }

    /// <summary>A publish the door accepts hands its receipt back unchanged.</summary>
    [Fact]
    public async Task A_publish_the_door_accepts_hands_its_receipt_back_unchanged()
    {
        var records = new RecordingDnsChallengeRecords();

        var published = await new AcmeDnsChallengeWriter([records]).PublishAsync(AccountId, "example.com", Value, CancellationToken.None);

        Assert.True(published.IsSuccess);
        Assert.Equal(Assert.Single(records.Published).Handle, published.Value);
    }
}
