using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Queries.CountSitesOnEndOfLifePhp;
using Maran.Modules.Sites.Tests.TestSupport;

namespace Maran.Modules.Sites.Tests.Queries.CountSitesOnEndOfLifePhp;

/// <summary>Behavioral contract of <see cref="CountSitesOnEndOfLifePhpQueryHandler"/>.</summary>
public sealed class CountSitesOnEndOfLifePhpQueryHandlerTests
{
    /// <summary>The fixed day every verdict below is read against.</summary>
    private static readonly DateTimeOffset Today = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Only sites on an end-of-life version are counted: a supported version, a security-only version,
    /// a version the lifecycle table has no row for, and a site with no PHP version are not.
    /// </summary>
    /// <remarks>
    /// Two sites share the end-of-life version <c>7.4</c> and one is on <c>8.0</c>, so the answer is a
    /// count of SITES (three), not of end-of-life versions (two).
    /// </remarks>
    [Fact]
    public async Task Only_sites_on_an_end_of_life_version_are_counted_at_a_fixed_clock()
    {
        var database = Guid.NewGuid().ToString();
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.Sites.AddRange(
                SitesTestContext.PhpSite(Guid.NewGuid(), "supported.example.com", "8.4"),
                SitesTestContext.PhpSite(Guid.NewGuid(), "security-only.example.com", "8.3"),
                SitesTestContext.PhpSite(Guid.NewGuid(), "eol-one.example.com", "7.4"),
                SitesTestContext.PhpSite(Guid.NewGuid(), "eol-two.example.com", "7.4"),
                SitesTestContext.PhpSite(Guid.NewGuid(), "eol-other.example.com", "8.0"),
                SitesTestContext.PhpSite(Guid.NewGuid(), "unknown-version.example.com", "9.9"),
                SitesTestContext.PhpSite(Guid.NewGuid(), "no-php.example.com", string.Empty));
            await seed.SaveChangesAsync();
        }

        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var result = await HandlerFor(context, Today).HandleAsync(
            new CountSitesOnEndOfLifePhpQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value);
    }

    /// <summary>
    /// A server whose sites are all on supported, security-only or unknown versions counts zero: the
    /// inverse control, without which a handler counting every site would pass the test above.
    /// </summary>
    [Fact]
    public async Task Sites_that_are_not_on_an_end_of_life_version_count_zero()
    {
        var database = Guid.NewGuid().ToString();
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.Sites.AddRange(
                SitesTestContext.PhpSite(Guid.NewGuid(), "supported.example.com", "8.4"),
                SitesTestContext.PhpSite(Guid.NewGuid(), "security-only.example.com", "8.3"),
                SitesTestContext.PhpSite(Guid.NewGuid(), "unknown-version.example.com", "9.9"));
            await seed.SaveChangesAsync();
        }

        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var result = await HandlerFor(context, Today).HandleAsync(
            new CountSitesOnEndOfLifePhpQuery(), CancellationToken.None);

        Assert.Equal(0, result.Value);
    }

    /// <summary>A server with no sites at all counts zero rather than failing.</summary>
    [Fact]
    public async Task A_server_with_no_sites_counts_zero()
    {
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin());

        var result = await HandlerFor(context, Today).HandleAsync(
            new CountSitesOnEndOfLifePhpQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
    }

    /// <summary>
    /// The verdict follows the panel's clock, not the ambient one: the same site is not end of life
    /// before its version's support ended and is after.
    /// </summary>
    [Fact]
    public async Task The_verdict_follows_the_injected_clock()
    {
        var database = Guid.NewGuid().ToString();
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.Sites.Add(SitesTestContext.PhpSite(Guid.NewGuid(), "old.example.com", "7.4"));
            await seed.SaveChangesAsync();
        }

        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var before = await HandlerFor(context, new DateTimeOffset(2021, 6, 1, 0, 0, 0, TimeSpan.Zero))
            .HandleAsync(new CountSitesOnEndOfLifePhpQuery(), CancellationToken.None);
        var after = await HandlerFor(context, Today)
            .HandleAsync(new CountSitesOnEndOfLifePhpQuery(), CancellationToken.None);

        Assert.Equal(0, before.Value);
        Assert.Equal(1, after.Value);
    }

    /// <summary>A customer's count is of their own sites only; another account's end-of-life site is hidden.</summary>
    [Fact]
    public async Task A_customers_count_is_of_their_own_sites_only()
    {
        var mine = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.Sites.Add(SitesTestContext.PhpSite(mine, "mine.example.com", "7.4"));
            seed.Sites.Add(SitesTestContext.PhpSite(mine, "mine-current.example.com", "8.4"));
            seed.Sites.Add(SitesTestContext.PhpSite(Guid.NewGuid(), "theirs-one.example.com", "7.4"));
            seed.Sites.Add(SitesTestContext.PhpSite(Guid.NewGuid(), "theirs-two.example.com", "8.0"));
            await seed.SaveChangesAsync();
        }

        await using var customer = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);
        await using var administrator = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var theirs = await HandlerFor(customer, Today).HandleAsync(
            new CountSitesOnEndOfLifePhpQuery(), CancellationToken.None);
        var all = await HandlerFor(administrator, Today).HandleAsync(
            new CountSitesOnEndOfLifePhpQuery(), CancellationToken.None);

        Assert.Equal(1, theirs.Value);
        Assert.Equal(3, all.Value);
    }

    /// <summary>Builds the handler over a context and a clock fixed at <paramref name="now"/>.</summary>
    /// <param name="context">The module's database context, bound to the caller under test.</param>
    /// <param name="now">The instant the clock reports.</param>
    /// <returns>The handler.</returns>
    private static CountSitesOnEndOfLifePhpQueryHandler HandlerFor(SitesDbContext context, DateTimeOffset now)
    {
        return new CountSitesOnEndOfLifePhpQueryHandler(context, new FakeClock(now));
    }
}
