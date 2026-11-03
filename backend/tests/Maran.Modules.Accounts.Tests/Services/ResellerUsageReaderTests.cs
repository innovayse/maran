using Maran.Modules.Accounts.Models;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;

namespace Maran.Modules.Accounts.Tests.Services;

/// <summary>What <see cref="ResellerUsageReader"/> measures from the rows.</summary>
public sealed class ResellerUsageReaderTests
{
    /// <summary>A reseller that owns nothing is measured as zero.</summary>
    [Fact]
    public async Task A_reseller_that_owns_nothing_is_measured_as_zero()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope());
        var reader = new ResellerUsageReader(fixture.DbContext);

        var usage = await reader.ReadAsync(fixture.Reseller.Id, CancellationToken.None);

        Assert.Equal(ResellerUsage.None, usage);
    }

    /// <summary>Accounts in any status, plans and the disk their plans promise are counted.</summary>
    [Fact]
    public async Task Accounts_in_any_status_plans_and_disk_are_counted()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope());
        var small = fixture.AddPlan(100, fixture.Reseller.Id);
        var large = fixture.AddPlan(400, fixture.Reseller.Id);
        fixture.AddAccount(small, fixture.Reseller.Id);
        fixture.AddAccount(large, fixture.Reseller.Id, suspended: true);
        fixture.AddAccount(large, fixture.Reseller.Id);
        var reader = new ResellerUsageReader(fixture.DbContext);

        var usage = await reader.ReadAsync(fixture.Reseller.Id, CancellationToken.None);

        Assert.Equal(new ResellerUsage(3, 2, 900), usage);
    }

    /// <summary>What the administrator and other resellers own is not counted.</summary>
    [Fact]
    public async Task What_other_owners_own_is_not_counted()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope());
        var adminPlan = fixture.AddPlan(777, null);
        fixture.AddAccount(adminPlan, null);
        var otherId = Guid.NewGuid();
        var otherPlan = fixture.AddPlan(888, otherId);
        fixture.AddAccount(otherPlan, otherId);
        var reader = new ResellerUsageReader(fixture.DbContext);

        var usage = await reader.ReadAsync(fixture.Reseller.Id, CancellationToken.None);

        Assert.Equal(ResellerUsage.None, usage);
    }

    /// <summary>Several resellers are measured together, and one that owns nothing has no entry.</summary>
    [Fact]
    public async Task Several_resellers_are_measured_together_and_an_empty_one_has_no_entry()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope());
        var other = ResellerTestData.Reseller("other");
        var empty = ResellerTestData.Reseller("empty");
        fixture.DbContext.Resellers.AddRange(other, empty);
        await fixture.DbContext.SaveChangesAsync();
        var mine = fixture.AddPlan(100, fixture.Reseller.Id);
        fixture.AddAccount(mine, fixture.Reseller.Id);
        var theirs = fixture.AddPlan(300, other.Id);
        fixture.AddAccount(theirs, other.Id);
        fixture.AddAccount(theirs, other.Id);
        var reader = new ResellerUsageReader(fixture.DbContext);

        var usage = await reader.ReadManyAsync(
            [fixture.Reseller.Id, other.Id, empty.Id],
            CancellationToken.None);

        Assert.Equal(new ResellerUsage(1, 1, 100), usage[fixture.Reseller.Id]);
        Assert.Equal(new ResellerUsage(2, 1, 600), usage[other.Id]);
        Assert.False(usage.ContainsKey(empty.Id));
    }

    /// <summary>A reseller owning plans but no accounts still has an entry.</summary>
    [Fact]
    public async Task A_reseller_owning_only_plans_still_has_an_entry()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope());
        fixture.AddPlan(100, fixture.Reseller.Id);
        var reader = new ResellerUsageReader(fixture.DbContext);

        var usage = await reader.ReadManyAsync([fixture.Reseller.Id], CancellationToken.None);

        Assert.Equal(new ResellerUsage(0, 1, 0), usage[fixture.Reseller.Id]);
    }

    /// <summary>Asking about nobody answers nothing.</summary>
    [Fact]
    public async Task Asking_about_nobody_answers_nothing()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope());
        var reader = new ResellerUsageReader(fixture.DbContext);

        Assert.Empty(await reader.ReadManyAsync([], CancellationToken.None));
    }

    /// <summary>The disk is summed as a long, so a few large quotas do not overflow an int.</summary>
    [Fact]
    public async Task The_disk_is_summed_without_overflowing_an_int()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope());
        var huge = fixture.AddPlan(int.MaxValue, fixture.Reseller.Id);
        fixture.AddAccount(huge, fixture.Reseller.Id);
        fixture.AddAccount(huge, fixture.Reseller.Id);
        var reader = new ResellerUsageReader(fixture.DbContext);

        var usage = await reader.ReadAsync(fixture.Reseller.Id, CancellationToken.None);

        Assert.Equal(2L * int.MaxValue, usage.DiskAllocatedMb);
    }
}
