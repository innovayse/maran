using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Queries.ListPinnedPages;
using Maran.Modules.Identity.Tests.TestSupport;

namespace Maran.Modules.Identity.Tests.Queries.ListPinnedPages;

/// <summary>Behavioural contract of <see cref="ListPinnedPagesQueryHandler"/>.</summary>
public sealed class ListPinnedPagesQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The keys after the sites list and the dashboard were pinned, in pin order.</summary>
    private static readonly string[] SitesListThenDashboard = { "sites-list", "dashboard" };

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();

    /// <summary>Prepares the fixture before the tests run.</summary>
    public async Task InitializeAsync()
    {
        _context.PinnedPages.Add(new PinnedPage(Guid.NewGuid(), _owner, "dashboard", 1, Now));
        _context.PinnedPages.Add(new PinnedPage(Guid.NewGuid(), _owner, "sites-list", 0, Now));
        _context.PinnedPages.Add(new PinnedPage(Guid.NewGuid(), _other, "audit", 0, Now));
        await _context.SaveChangesAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    /// <summary>The caller reads their own pins by position and the limit beside them.</summary>
    [Fact]
    public async Task The_callers_pins_come_back_by_position_with_the_limit()
    {
        var result = await new ListPinnedPagesQueryHandler(_context)
            .HandleAsync(new ListPinnedPagesQuery(_owner), CancellationToken.None);

        Assert.Equal(SitesListThenDashboard, result.Value!.Items.Select(item =>
        {
            return item.ItemKey;
        }));
        Assert.Equal(PinnedPagePolicy.MaxPinsPerUser, result.Value.Limit);
    }

    /// <summary>A person who pinned nothing gets an empty list, not an error.</summary>
    [Fact]
    public async Task A_user_with_no_pins_gets_an_empty_list()
    {
        var result = await new ListPinnedPagesQueryHandler(_context)
            .HandleAsync(new ListPinnedPagesQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }
}
