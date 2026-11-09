using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Queries.GetMyBranding;
using Maran.Modules.Accounts.Tests.TestSupport;

namespace Maran.Modules.Accounts.Tests.Queries.GetMyBranding;

/// <summary>
/// Behavioral contract of <see cref="GetMyBrandingQueryHandler"/>: a customer reads the branding of the
/// reseller that owns its account and no other, and every caller the question does not apply to reads
/// the panel's own.
/// </summary>
public sealed class GetMyBrandingQueryHandlerTests
{
    /// <summary>The instant the seeded branding was written.</summary>
    private static readonly DateTimeOffset Written = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>What the first reseller set.</summary>
    private static readonly ResellerBrandingDto AlphaBranding = new("Alpha", "#2468dc", null);

    /// <summary>What the second reseller set.</summary>
    private static readonly ResellerBrandingDto BetaBranding = new("Beta", "#8a1f1f", null);

    /// <summary>The nothing-set answer.</summary>
    private static readonly ResellerBrandingDto Empty = new(null, null, null);

    /// <summary>Seeds the two-reseller world with a branding for each reseller.</summary>
    private static async Task<TwoResellerWorld> SeedAsync()
    {
        var world = await TwoResellerWorld.SeedAsync();
        await using var seeding = world.ContextFor(FakeCurrentUser.Admin());
        seeding.ResellerBrandings.AddRange(
            new ResellerBranding(world.Alpha.Id, AlphaBranding.DisplayName, AlphaBranding.AccentColor, null, Written),
            new ResellerBranding(world.Beta.Id, BetaBranding.DisplayName, BetaBranding.AccentColor, null, Written));
        await seeding.SaveChangesAsync();
        return world;
    }

    /// <summary>Asks as <paramref name="caller"/> over the world's rows.</summary>
    /// <param name="world">The seeded world.</param>
    /// <param name="caller">The principal the request runs as.</param>
    private static async Task<ResellerBrandingDto> AskAsync(TwoResellerWorld world, FakeCurrentUser caller)
    {
        await using var dbContext = world.ContextFor(caller);
        var handler = new GetMyBrandingQueryHandler(caller, dbContext);
        var result = await handler.HandleAsync(new GetMyBrandingQuery(), CancellationToken.None);
        return result.Value;
    }

    /// <summary>A customer of a reseller reads that reseller's branding: the accepting control.</summary>
    [Fact]
    public async Task A_customer_reads_the_branding_of_the_reseller_that_owns_its_account()
    {
        var world = await SeedAsync();

        Assert.Equal(AlphaBranding, await AskAsync(world, FakeCurrentUser.Customer(world.AlphaAccount.Id)));
        Assert.Equal(BetaBranding, await AskAsync(world, FakeCurrentUser.Customer(world.BetaAccount.Id)));
    }

    /// <summary>A customer of the administrator reads the panel's own, never a reseller's: the refusing control.</summary>
    [Fact]
    public async Task A_customer_of_the_administrator_reads_the_panels_own()
    {
        var world = await SeedAsync();

        Assert.Equal(Empty, await AskAsync(world, FakeCurrentUser.Customer(world.AdminAccount.Id)));
    }

    /// <summary>A customer of a reseller that set nothing reads the panel's own.</summary>
    [Fact]
    public async Task A_customer_of_a_reseller_that_set_nothing_reads_the_panels_own()
    {
        var world = await TwoResellerWorld.SeedAsync();

        Assert.Equal(Empty, await AskAsync(world, FakeCurrentUser.Customer(world.AlphaAccount.Id)));
    }

    /// <summary>An administrator, a reseller, an anonymous caller and an accountless customer read the panel's own.</summary>
    [Fact]
    public async Task Every_caller_that_owns_no_account_reads_the_panels_own()
    {
        var world = await SeedAsync();

        Assert.Equal(Empty, await AskAsync(world, FakeCurrentUser.Admin()));
        Assert.Equal(Empty, await AskAsync(world, world.AsAlpha()));
        Assert.Equal(Empty, await AskAsync(world, FakeCurrentUser.Anonymous()));
        Assert.Equal(Empty, await AskAsync(world, FakeCurrentUser.AccountlessCustomer()));
    }
}
