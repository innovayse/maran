using Maran.Modules.Identity.Commands.SaveDashboardLayout;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Commands.SaveDashboardLayout;

/// <summary>Behavioural contract of <see cref="SaveDashboardLayoutCommandHandler"/>.</summary>
public sealed class SaveDashboardLayoutCommandHandlerTests : IAsyncLifetime
{
    /// <summary>The hidden flags of the two saved widgets, in saved order.</summary>
    private static readonly bool[] VisibleThenHidden = { false, true };

    /// <summary>The widget keys of the first save, in the order sent.</summary>
    private static readonly string[] CpuThenMemory = { "cpu", "memory" };

    /// <summary>The widget keys of the second save, in the order sent.</summary>
    private static readonly string[] MemoryThenNetwork = { "memory", "network" };

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

    private SaveDashboardLayoutCommandHandler NewHandler()
    {
        return new SaveDashboardLayoutCommandHandler(
            _context,
            new IdentityAuditJournal(_audit, new StubCurrentUser(_owner, "owner")));
    }

    private static List<DashboardLayoutEntryDto> Entries(params (string Key, bool Hidden)[] entries)
    {
        return entries.Select(entry =>
        {
            return new DashboardLayoutEntryDto(entry.Key, entry.Hidden);
        }).ToList();
    }

    private SaveDashboardLayoutCommand Save(
        List<DashboardLayoutEntryDto> widgets,
        List<DashboardLayoutEntryDto>? actions = null,
        Guid? user = null)
    {
        return new SaveDashboardLayoutCommand(widgets, actions ?? new List<DashboardLayoutEntryDto>(), user ?? _owner, "203.0.113.7", "agent");
    }

    private async Task<List<DashboardPreference>> StoredAsync(Guid user, DashboardPreferenceKind kind)
    {
        return (await _context.DashboardPreferences.Where(row => row.UserId == user && row.Kind == kind).ToListAsync())
            .OrderBy(row =>
            {
                return row.Position;
            })
            .ToList();
    }

    /// <summary>Saving stores both lists in the order sent with their hidden flags, and is audited.</summary>
    [Fact]
    public async Task Saving_stores_both_lists_in_order_with_their_flags_and_is_audited()
    {
        var result = await NewHandler().HandleAsync(
            Save(Entries(("cpu", false), ("memory", true)), Entries(("addSite", false))),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var widgets = await StoredAsync(_owner, DashboardPreferenceKind.Widget);
        Assert.Equal(CpuThenMemory, widgets.Select(row =>
        {
            return row.Key;
        }));
        Assert.Equal(VisibleThenHidden, widgets.Select(row =>
        {
            return row.Hidden;
        }));
        Assert.Equal("addSite", Assert.Single(await StoredAsync(_owner, DashboardPreferenceKind.Action)).Key);
        var entry = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.DashboardLayoutSaved, entry.Action);
        Assert.Equal("widgets=2 actions=1", entry.Subject);
        Assert.Equal(_owner, entry.ActorUserId);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A second save replaces the first: an entry not sent again is forgotten, a repeated one moves in place.</summary>
    [Fact]
    public async Task A_second_save_replaces_the_first()
    {
        await NewHandler().HandleAsync(Save(Entries(("cpu", false), ("memory", false))), CancellationToken.None);
        var firstMemoryRow = (await StoredAsync(_owner, DashboardPreferenceKind.Widget)).Single(row =>
        {
            return row.Key == "memory";
        }).Id;

        var result = await NewHandler().HandleAsync(
            Save(Entries(("memory", true), ("network", false))),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var widgets = await StoredAsync(_owner, DashboardPreferenceKind.Widget);
        Assert.Equal(MemoryThenNetwork, widgets.Select(row =>
        {
            return row.Key;
        }));
        Assert.True(widgets[0].Hidden);
        Assert.Equal(firstMemoryRow, widgets[0].Id);
    }

    /// <summary>A key no panel default names is stored as sent.</summary>
    [Fact]
    public async Task A_key_the_panel_has_no_default_for_is_kept()
    {
        await NewHandler().HandleAsync(Save(Entries(("mailQueue", false))), CancellationToken.None);

        Assert.Equal("mailQueue", Assert.Single(await StoredAsync(_owner, DashboardPreferenceKind.Widget)).Key);
    }

    /// <summary>Saving an empty layout clears what the caller stored.</summary>
    [Fact]
    public async Task Saving_empty_lists_clears_the_stored_layout()
    {
        await NewHandler().HandleAsync(Save(Entries(("cpu", false)), Entries(("addSite", false))), CancellationToken.None);

        await NewHandler().HandleAsync(Save(Entries()), CancellationToken.None);

        Assert.Empty(await _context.DashboardPreferences.Where(row => row.UserId == _owner).ToListAsync());
    }

    /// <summary>One person's save never touches another person's rows.</summary>
    [Fact]
    public async Task A_save_leaves_another_users_layout_alone()
    {
        await NewHandler().HandleAsync(Save(Entries(("cpu", true)), user: _other), CancellationToken.None);

        await NewHandler().HandleAsync(Save(Entries(("memory", false))), CancellationToken.None);

        var theirs = Assert.Single(await StoredAsync(_other, DashboardPreferenceKind.Widget));
        Assert.Equal("cpu", theirs.Key);
        Assert.True(theirs.Hidden);
        Assert.Equal("memory", Assert.Single(await StoredAsync(_owner, DashboardPreferenceKind.Widget)).Key);
    }
}
