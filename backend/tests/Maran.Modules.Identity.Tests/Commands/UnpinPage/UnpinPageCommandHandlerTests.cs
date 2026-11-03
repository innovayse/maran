using Maran.Modules.Identity.Commands.UnpinPage;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Commands.UnpinPage;

/// <summary>Behavioural contract of <see cref="UnpinPageCommandHandler"/>.</summary>
public sealed class UnpinPageCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The keys of a person left holding only the dashboard.</summary>
    private static readonly string[] DashboardOnly = { "dashboard" };

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly RecordingAuditWriter _audit = new();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();

    /// <summary>Prepares the fixture before the tests run.</summary>
    public async Task InitializeAsync()
    {
        _context.PinnedPages.Add(new PinnedPage(Guid.NewGuid(), _owner, "sites-list", 0, Now));
        _context.PinnedPages.Add(new PinnedPage(Guid.NewGuid(), _owner, "dashboard", 1, Now));
        _context.PinnedPages.Add(new PinnedPage(Guid.NewGuid(), _other, "dashboard", 0, Now));
        await _context.SaveChangesAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    private UnpinPageCommandHandler NewHandler()
    {
        return new UnpinPageCommandHandler(
            _context,
            new IdentityAuditJournal(_audit, new StubCurrentUser(_owner, "owner")));
    }

    /// <summary>Unpinning removes the page, answers with what is left and is audited.</summary>
    [Fact]
    public async Task Unpinning_a_page_removes_it_answers_with_the_rest_and_is_audited()
    {
        var result = await NewHandler().HandleAsync(
            new UnpinPageCommand("sites-list", _owner, "203.0.113.7", "agent"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DashboardOnly, result.Value!.Items.Select(item =>
        {
            return item.ItemKey;
        }));
        Assert.Equal(2, await _context.PinnedPages.CountAsync());
        var entry = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.PageUnpinned, entry.Action);
        Assert.Equal("sites-list", entry.Subject);
    }

    /// <summary>Unpinning a page that is not pinned answers not found and writes nothing.</summary>
    [Fact]
    public async Task Unpinning_a_page_that_is_not_pinned_answers_not_found()
    {
        var result = await NewHandler().HandleAsync(
            new UnpinPageCommand("monitoring-metrics", _owner, "203.0.113.7", "agent"), CancellationToken.None);

        Assert.Equal("PinnedPageNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(_audit.Written);
    }

    /// <summary>Another user's pin of the same page is neither visible nor removed.</summary>
    [Fact]
    public async Task Another_users_pin_of_the_same_page_is_left_alone()
    {
        await NewHandler().HandleAsync(
            new UnpinPageCommand("dashboard", _owner, "203.0.113.7", "agent"), CancellationToken.None);

        Assert.Equal(_other, (await _context.PinnedPages.SingleAsync(pin => pin.ItemKey == "dashboard")).UserId);

        var again = await NewHandler().HandleAsync(
            new UnpinPageCommand("dashboard", _owner, "203.0.113.7", "agent"), CancellationToken.None);

        Assert.Equal("PinnedPageNotFound", again.Error!.Code);
        Assert.Equal(1, await _context.PinnedPages.CountAsync(pin => pin.ItemKey == "dashboard"));
    }
}
