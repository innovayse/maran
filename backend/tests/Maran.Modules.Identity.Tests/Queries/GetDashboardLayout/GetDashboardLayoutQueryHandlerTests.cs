using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Queries.GetDashboardLayout;
using Maran.Modules.Identity.Tests.TestSupport;

namespace Maran.Modules.Identity.Tests.Queries.GetDashboardLayout;

/// <summary>Behavioural contract of <see cref="GetDashboardLayoutQueryHandler"/>.</summary>
public sealed class GetDashboardLayoutQueryHandlerTests : IAsyncLifetime
{
    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();

    /// <summary>Prepares the fixture before the tests run.</summary>
    public async Task InitializeAsync()
    {
        _context.DashboardPreferences.Add(new DashboardPreference(
            Guid.NewGuid(), _owner, DashboardPreferenceKind.Widget, "network", 0, hidden: true));
        _context.DashboardPreferences.Add(new DashboardPreference(
            Guid.NewGuid(), _owner, DashboardPreferenceKind.Widget, "mailQueue", 1, hidden: false));
        _context.DashboardPreferences.Add(new DashboardPreference(
            Guid.NewGuid(), _other, DashboardPreferenceKind.Widget, "cpu", 0, hidden: true));
        await _context.SaveChangesAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    /// <summary>The caller's own choices lead, an unknown key is returned in place, and the defaults they never touched follow.</summary>
    [Fact]
    public async Task The_callers_choices_lead_and_untouched_defaults_follow()
    {
        var result = await new GetDashboardLayoutQueryHandler(_context)
            .HandleAsync(new GetDashboardLayoutQuery(_owner, ForAdministrator: true), CancellationToken.None);

        var widgets = result.Value!.Widgets;
        Assert.Equal("network", widgets[0].Key);
        Assert.True(widgets[0].Hidden);
        Assert.Equal("mailQueue", widgets[1].Key);
        Assert.Contains(widgets, entry =>
        {
            return entry.Key == "cpu" && !entry.Hidden;
        });
        Assert.Equal(1, widgets.Count(entry =>
        {
            return entry.Key == "network";
        }));
        Assert.Equal(6, result.Value.Actions.Count);
    }

    /// <summary>Another person's hidden CPU card does not show in the caller's layout.</summary>
    [Fact]
    public async Task Another_users_choices_are_not_returned()
    {
        var result = await new GetDashboardLayoutQueryHandler(_context)
            .HandleAsync(new GetDashboardLayoutQuery(_owner, ForAdministrator: true), CancellationToken.None);

        Assert.False(result.Value!.Widgets.Single(entry =>
        {
            return entry.Key == "cpu";
        }).Hidden);
    }

    /// <summary>A caller who is not an administrator and stored nothing gets two empty lists, not an error.</summary>
    [Fact]
    public async Task A_caller_who_is_not_an_administrator_and_stored_nothing_gets_empty_lists()
    {
        var result = await new GetDashboardLayoutQueryHandler(_context)
            .HandleAsync(new GetDashboardLayoutQuery(Guid.NewGuid(), ForAdministrator: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Widgets);
        Assert.Empty(result.Value.Actions);
    }
}
