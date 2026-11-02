using Maran.Modules.Identity.Commands.PinPage;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Commands.PinPage;

/// <summary>Behavioural contract of <see cref="PinPageCommandHandler"/>.</summary>
public sealed class PinPageCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly RecordingAuditWriter _audit = new();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();

    /// <summary>Prepares the fixture before the tests run.</summary>
    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    private PinPageCommandHandler NewHandler()
    {
        return new PinPageCommandHandler(
            _context,
            new FakeClock(Now),
            new IdentityAuditJournal(_audit, new StubCurrentUser(_owner, "owner")));
    }

    private PinPageCommand Pin(string key)
    {
        return new PinPageCommand(key, _owner, "203.0.113.7", "agent");
    }

    /// <summary>Pinning stores the page for the caller, answers with the list and is audited.</summary>
    [Fact]
    public async Task Pinning_a_page_stores_it_answers_with_the_list_and_is_audited()
    {
        var result = await NewHandler().HandleAsync(Pin("accounts-list"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "accounts-list" }, result.Value!.Items.Select(item => item.ItemKey));
        Assert.Equal(PinnedPagePolicy.MaxPinsPerUser, result.Value.Limit);
        var stored = await _context.PinnedPages.SingleAsync();
        Assert.Equal(_owner, stored.UserId);
        var entry = Assert.Single(_audit.Written);
        Assert.Equal(AuditActions.PagePinned, entry.Action);
        Assert.Equal("accounts-list", entry.Subject);
        Assert.Equal(_owner, entry.ActorUserId);
    }

    /// <summary>Pins keep the order they were made in.</summary>
    [Fact]
    public async Task Pins_are_listed_in_the_order_they_were_made()
    {
        await NewHandler().HandleAsync(Pin("sites-list"), CancellationToken.None);
        var result = await NewHandler().HandleAsync(Pin("dashboard"), CancellationToken.None);

        Assert.Equal(new[] { "sites-list", "dashboard" }, result.Value!.Items.Select(item => item.ItemKey));
    }

    /// <summary>Pinning the same page twice is a conflict and writes nothing more.</summary>
    [Fact]
    public async Task Pinning_a_page_twice_is_refused_as_a_conflict()
    {
        await NewHandler().HandleAsync(Pin("dashboard"), CancellationToken.None);

        var result = await NewHandler().HandleAsync(Pin("dashboard"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("PinnedPageAlreadyPinned", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(1, await _context.PinnedPages.CountAsync());
        Assert.Single(_audit.Written);
    }

    /// <summary>The page after the limit is refused and nothing is stored or audited for it.</summary>
    [Fact]
    public async Task Pinning_beyond_the_limit_is_refused_as_a_conflict()
    {
        for (var index = 0; index < PinnedPagePolicy.MaxPinsPerUser; index++)
        {
            await NewHandler().HandleAsync(Pin($"page-{index}"), CancellationToken.None);
        }

        var result = await NewHandler().HandleAsync(Pin("one-too-many"), CancellationToken.None);

        Assert.Equal("PinnedPageLimitReached", result.Error!.Code);
        Assert.Equal(PinnedPagePolicy.MaxPinsPerUser, await _context.PinnedPages.CountAsync());
        Assert.Equal(PinnedPagePolicy.MaxPinsPerUser, _audit.Written.Count);
    }

    /// <summary>Another user's pins neither count against the caller nor appear in the answer.</summary>
    [Fact]
    public async Task Another_users_pins_do_not_count_and_are_not_listed()
    {
        for (var index = 0; index < PinnedPagePolicy.MaxPinsPerUser; index++)
        {
            _context.PinnedPages.Add(new PinnedPage(Guid.NewGuid(), _other, $"page-{index}", index, Now));
        }

        await _context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Pin("page-0"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "page-0" }, result.Value!.Items.Select(item => item.ItemKey));
    }
}
