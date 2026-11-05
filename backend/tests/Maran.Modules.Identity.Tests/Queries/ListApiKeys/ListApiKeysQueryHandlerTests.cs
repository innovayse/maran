using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Queries.ListApiKeys;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Identity.Tests.Queries.ListApiKeys;

/// <summary>Behavioural contract of <see cref="ListApiKeysQueryHandler"/>.</summary>
/// <remarks>
/// UNOBSERVED HERE: the search by name. It is an <c>ILIKE</c>, which only PostgreSQL translates, so the
/// InMemory provider cannot run it; the escaping of the term is covered where <c>LikePattern</c> is.
/// </remarks>
public sealed class ListApiKeysQueryHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly FakeClock _clock = new(Now);

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>Builds the handler under test over the fixture's context and clock.</summary>
    private ListApiKeysQueryHandler NewHandler()
    {
        return new ListApiKeysQueryHandler(_context, _clock);
    }

    /// <summary>Runs a list query and returns the names on the page it answers with.</summary>
    private async Task<List<string>> NamesAsync(ListApiKeysQuery query)
    {
        var result = await NewHandler().HandleAsync(query, CancellationToken.None);

        return result.Value.Items.Select(key => { return key.Name; }).ToList();
    }

    /// <summary>The default list shows every key, newest first, with the derived status of each.</summary>
    [Fact]
    public async Task The_default_list_shows_every_key_newest_first_with_its_derived_status()
    {
        await ApiKeySeed.AddAsync(_context, Now.AddDays(-3), name: "old-active");
        await ApiKeySeed.AddAsync(_context, Now.AddDays(-2), name: "revoked", revoked: true);
        await ApiKeySeed.AddAsync(_context, Now.AddDays(-1), name: "expired", expiresAt: Now.AddHours(-1));

        var result = await NewHandler().HandleAsync(new ListApiKeysQuery(), CancellationToken.None);

        Assert.Equal(3, result.Value.Total);
        Assert.Equal(["expired", "revoked", "old-active"], result.Value.Items.Select(key => { return key.Name; }));
        Assert.Equal(
            [ApiKeyStatus.Expired, ApiKeyStatus.Revoked, ApiKeyStatus.Active],
            result.Value.Items.Select(key => { return key.Status; }));
    }

    /// <summary>Each status filter returns exactly the keys in that state.</summary>
    [Fact]
    public async Task Each_status_filter_returns_exactly_the_keys_in_that_state()
    {
        await ApiKeySeed.AddAsync(_context, Now.AddDays(-4), name: "active-open");
        await ApiKeySeed.AddAsync(_context, Now.AddDays(-3), name: "active-dated", expiresAt: Now.AddDays(1));
        await ApiKeySeed.AddAsync(_context, Now.AddDays(-2), name: "revoked", revoked: true);
        await ApiKeySeed.AddAsync(_context, Now.AddDays(-1), name: "expired", expiresAt: Now.AddHours(-1));
        await ApiKeySeed.AddAsync(_context, Now.AddDays(-5), name: "revoked-and-expired", expiresAt: Now.AddHours(-2), revoked: true);

        var active = await NamesAsync(new ListApiKeysQuery(Status: FilterValue.Of(ApiKeyStatusFilters.Active)));
        var revoked = await NamesAsync(new ListApiKeysQuery(Status: FilterValue.Of(ApiKeyStatusFilters.Revoked)));
        var expired = await NamesAsync(new ListApiKeysQuery(Status: FilterValue.Of(ApiKeyStatusFilters.Expired)));

        Assert.Equal(["active-dated", "active-open"], active);
        Assert.Equal(["revoked", "revoked-and-expired"], revoked);
        Assert.Equal(["expired"], expired);
    }

    /// <summary>A key whose expiry passes as the clock advances moves from the active filter to the expired one with no row change.</summary>
    [Fact]
    public async Task A_key_whose_expiry_passes_with_the_clock_moves_from_active_to_expired()
    {
        await ApiKeySeed.AddAsync(_context, Now.AddDays(-1), name: "short", expiresAt: Now.AddMinutes(10));
        var active = new ListApiKeysQuery(Status: FilterValue.Of(ApiKeyStatusFilters.Active));
        var expired = new ListApiKeysQuery(Status: FilterValue.Of(ApiKeyStatusFilters.Expired));

        Assert.Equal(["short"], await NamesAsync(active));
        Assert.Empty(await NamesAsync(expired));

        _clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Empty(await NamesAsync(active));
        Assert.Equal(["short"], await NamesAsync(expired));
    }

    /// <summary>Sorting by name ascending and by last use descending orders the rows as asked.</summary>
    [Fact]
    public async Task Sorting_by_name_and_by_last_use_orders_the_rows_as_asked()
    {
        var (beta, _) = await ApiKeySeed.AddAsync(_context, Now.AddDays(-3), name: "beta");
        var (alpha, _) = await ApiKeySeed.AddAsync(_context, Now.AddDays(-2), name: "alpha");
        await ApiKeySeed.AddAsync(_context, Now.AddDays(-1), name: "gamma");
        beta.RecordUse(Now.AddHours(-1), "203.0.113.7");
        alpha.RecordUse(Now.AddHours(-5), "203.0.113.8");
        await _context.SaveChangesAsync();

        var byName = await NamesAsync(new ListApiKeysQuery(Sort: SortRequest.Parse(ApiKeySortFields.Name)));
        var byLastUseDescending = await NamesAsync(new ListApiKeysQuery(Sort: SortRequest.Parse("-" + ApiKeySortFields.LastUsedAt)));
        var byCreatedAscending = await NamesAsync(new ListApiKeysQuery(Sort: SortRequest.Parse(ApiKeySortFields.CreatedAt)));

        Assert.Equal(["alpha", "beta", "gamma"], byName);
        Assert.Equal("beta", byLastUseDescending[0]);
        Assert.Equal(["beta", "alpha", "gamma"], byCreatedAscending);
    }

    /// <summary>Paging returns the requested slice and the total across all pages.</summary>
    [Fact]
    public async Task Paging_returns_the_requested_slice_and_the_total_across_pages()
    {
        for (var i = 0; i < 12; i++)
        {
            await ApiKeySeed.AddAsync(_context, Now.AddMinutes(-i), name: $"key-{i:D2}");
        }

        var first = await NewHandler().HandleAsync(new ListApiKeysQuery(PageSize: 10), CancellationToken.None);
        var second = await NewHandler().HandleAsync(new ListApiKeysQuery(Page: 2, PageSize: 10), CancellationToken.None);

        Assert.Equal(10, first.Value.Items.Count);
        Assert.Equal(["key-10", "key-11"], second.Value.Items.Select(key => { return key.Name; }));
        Assert.Equal(12, first.Value.Total);
        Assert.Equal(12, second.Value.Total);
    }

    /// <summary>The list carries the public prefix and bindings of a key, and its row shape has no digest member.</summary>
    [Fact]
    public async Task The_list_carries_the_prefix_and_bindings_of_a_key()
    {
        var (key, _) = await ApiKeySeed.AddAsync(_context, Now.AddDays(-1), allowedCidrs: ["203.0.113.0/24"]);

        var row = (await NewHandler().HandleAsync(new ListApiKeysQuery(), CancellationToken.None)).Value.Items.Single();

        Assert.Equal(key.Prefix, row.Prefix);
        Assert.Equal(["203.0.113.0/24"], row.AllowedCidrs);
        Assert.DoesNotContain("TokenHash", typeof(ApiKeyDto).GetProperties().Select(property => { return property.Name; }));
    }
}
