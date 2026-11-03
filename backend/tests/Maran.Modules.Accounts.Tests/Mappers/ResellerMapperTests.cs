using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Models;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Tests.Mappers;

/// <summary>
/// Behavioral contract of <see cref="ResellerMapper"/>: it restates a reseller, its usage and its
/// envelope on the wire and back without deciding anything.
/// </summary>
public sealed class ResellerMapperTests
{
    /// <summary>The state names the tests register, so a state reads as its localized word.</summary>
    private static readonly StubDisplayNames DisplayNames = new StubDisplayNames()
        .With("ResellerStatusActive", "Live")
        .With("ResellerStatusSuspended", "Paused");

    /// <summary>A reseller's list row carries its identity, state name, creation time and usage.</summary>
    [Fact]
    public void A_list_row_carries_identity_state_name_creation_time_and_usage()
    {
        var reseller = ResellerTestData.Reseller("acme");
        reseller.Suspend();

        var dto = ResellerMapper.ToDto(reseller, new ResellerUsage(3, 2, 4_096), DisplayNames);

        Assert.Equal(reseller.Id, dto.Id);
        Assert.Equal("acme", dto.Name);
        Assert.Equal("acme@example.com", dto.ContactEmail);
        Assert.Equal(ResellerStatus.Suspended, dto.Status);
        Assert.Equal("Paused", dto.StatusName);
        Assert.Equal(ResellerTestData.CreatedAt, dto.CreatedAt);
        Assert.Equal(3, dto.AccountCount);
        Assert.Equal(2, dto.PlanCount);
        Assert.Equal(4_096, dto.DiskAllocatedMb);
    }

    /// <summary>An active reseller is named by the active word, the inverse control of the suspended one above.</summary>
    [Fact]
    public void An_active_reseller_is_named_by_the_active_word()
    {
        Assert.Equal("Live", ResellerMapper.StatusNameOf(ResellerStatus.Active, DisplayNames));
    }

    /// <summary>The detail row adds the language and the envelope to the list row's fields.</summary>
    [Fact]
    public void The_detail_row_adds_the_language_and_the_envelope()
    {
        var envelope = ResellerTestData.Envelope(maxAccounts: 7, diskPoolMb: 9_000, features: AccountFeature.Sites);
        var reseller = ResellerTestData.Reseller("acme", envelope);

        var dto = ResellerMapper.ToDetailDto(reseller, ResellerUsage.None, DisplayNames);

        Assert.Equal("en", dto.Language);
        Assert.Equal(7, dto.Envelope.MaxAccounts);
        Assert.Equal(9_000, dto.Envelope.DiskPoolMb);
        Assert.Equal([AccountFeature.Sites], dto.Envelope.Features);
        Assert.Equal(0, dto.AccountCount);
        Assert.Equal("Live", dto.StatusName);
    }

    /// <summary>An envelope survives the trip to the wire and back with every figure and ceiling intact.</summary>
    [Fact]
    public void An_envelope_survives_the_trip_to_the_wire_and_back()
    {
        var envelope = ResellerTestData.Envelope(
            maxAccounts: 1,
            diskPoolMb: 2,
            maxPlans: 3,
            maxDiskQuotaMb: 4,
            maxSites: 5,
            maxDatabases: 6,
            maxSftpUsers: 7,
            maxFtpUsers: 8,
            maxCronEntries: 9,
            maxPhpWorkersPerPool: 10,
            features: AccountFeature.Mail | AccountFeature.Backups,
            runtimeLimits: RuntimeLimits.Of(
                ResourceLimit.Limited(50),
                ResourceLimit.Unlimited(),
                ResourceLimit.Limited(200)));

        var back = ResellerMapper.ToDomain(ResellerMapper.ToEnvelopeDto(envelope));

        Assert.Equal(envelope, back);
    }

    /// <summary>The wire ceilings are limited where a cap exists and unlimited where none does.</summary>
    [Fact]
    public void The_wire_ceilings_are_limited_where_a_cap_exists_and_unlimited_where_none_does()
    {
        var envelope = ResellerTestData.Envelope(
            runtimeLimits: RuntimeLimits.Of(
                ResourceLimit.Limited(50),
                ResourceLimit.Unlimited(),
                ResourceLimit.Limited(200)));

        var dto = ResellerMapper.ToEnvelopeDto(envelope);

        Assert.Equal(new LimitedResourceCeilingDto(50), dto.CpuQuotaPercent);
        Assert.IsType<UnlimitedResourceCeilingDto>(dto.MemoryMaxMb);
        Assert.Equal(new LimitedResourceCeilingDto(200), dto.MaxProcesses);
    }

    /// <summary>A wire envelope with a negative figure is refused as the domain refuses it.</summary>
    [Fact]
    public void A_wire_envelope_with_a_negative_figure_is_refused_by_the_domain()
    {
        var dto = ResellerEnvelopeDtoTestData.Valid() with { MaxPlans = -1 };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            return ResellerMapper.ToDomain(dto);
        });
    }
}
