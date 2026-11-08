using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Queries.GetResellerBranding;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Queries.GetResellerBranding;

/// <summary>
/// Behavioral contract of <see cref="GetResellerBrandingQueryHandler"/>: a reseller reads its own
/// branding for the settings form and never another's.
/// </summary>
public sealed class GetResellerBrandingQueryHandlerTests
{
    /// <summary>Asks as <paramref name="caller"/> over the world's rows.</summary>
    /// <param name="world">The seeded world.</param>
    /// <param name="caller">The principal the request runs as.</param>
    private static async Task<Result<ResellerBrandingDto>> AskAsync(TwoResellerWorld world, FakeCurrentUser caller)
    {
        await using var dbContext = world.ContextFor(caller);
        var handler = new GetResellerBrandingQueryHandler(dbContext, new ResellerScope(dbContext, caller));
        return await handler.HandleAsync(new GetResellerBrandingQuery(), CancellationToken.None);
    }

    /// <summary>A reseller reads its own branding, and a reseller that set none reads every member empty.</summary>
    [Fact]
    public async Task A_reseller_reads_its_own_branding_and_never_the_other_ones()
    {
        var world = await TwoResellerWorld.SeedAsync();
        await using (var seeding = world.ContextFor(FakeCurrentUser.Admin()))
        {
            seeding.ResellerBrandings.Add(new ResellerBranding(world.Beta.Id, "Beta", null, null, DateTimeOffset.UnixEpoch));
            await seeding.SaveChangesAsync();
        }

        var beta = await AskAsync(world, world.AsBeta());
        var alpha = await AskAsync(world, world.AsAlpha());

        Assert.Equal(new ResellerBrandingDto("Beta", null, null), beta.Value);
        Assert.Equal(new ResellerBrandingDto(null, null, null), alpha.Value);
    }

    /// <summary>A caller that is not a reseller is answered not found.</summary>
    [Fact]
    public async Task A_caller_that_is_not_a_reseller_is_answered_not_found()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var result = await AskAsync(world, FakeCurrentUser.Admin());

        Assert.Equal("ResellerNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }
}
