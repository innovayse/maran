using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Queries.ListExportGrants;
using Maran.Modules.ServerMigration.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.ServerMigration.Tests.Queries.ListExportGrants;

/// <summary>The pull grants list: its state filter and its rows agree with the entity's own answer, and no row holds a token (issue #127).</summary>
public sealed class ListExportGrantsQueryHandlerTests
{
    /// <summary>The moment the list is read.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The handler over a database holding one grant in each of the four states.</summary>
    /// <param name="name">A name for the in-memory database.</param>
    /// <returns>The handler and the grants by state.</returns>
    private static (ListExportGrantsQueryHandler Handler, Dictionary<ExportGrantState, ExportGrant> Grants) Build(string name)
    {
        var database = InMemoryMigrationDatabase.Open(name);
        var export = new AccountExport(
            Guid.NewGuid(), Guid.NewGuid(), "alice", new MigrationExportArtifact(Guid.NewGuid(), 10, new string('c', 64), 0),
            "[]", Guid.NewGuid(), Now.AddHours(-3), TimeSpan.FromDays(7));
        database.AccountExports.Add(export);
        ExportGrant Make(TimeSpan lifetime)
        {
            return new ExportGrant(Guid.NewGuid(), export.Id, Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), null, Now.AddHours(-2), lifetime);
        }

        var live = Make(TimeSpan.FromHours(5));
        var expired = Make(TimeSpan.FromMinutes(30));
        var used = Make(TimeSpan.FromHours(5));
        used.ConsumeArtifact(Now.AddHours(-1));
        var revoked = Make(TimeSpan.FromHours(5));
        revoked.Revoke(Now.AddHours(-1));
        database.ExportGrants.AddRange(live, expired, used, revoked);
        database.SaveChanges();

        return (
            new ListExportGrantsQueryHandler(database, new HandlerClock { UtcNow = Now }, new EchoDisplayNames()),
            new Dictionary<ExportGrantState, ExportGrant>
            {
                [ExportGrantState.Live] = live,
                [ExportGrantState.Expired] = expired,
                [ExportGrantState.Used] = used,
                [ExportGrantState.Revoked] = revoked,
            });
    }

    /// <summary>Filtering by each state returns exactly the grant the entity says is in that state, and the row says the same.</summary>
    /// <param name="state">The state's name.</param>
    [Theory]
    [InlineData("Live")]
    [InlineData("Expired")]
    [InlineData("Used")]
    [InlineData("Revoked")]
    public async Task Each_state_filter_returns_exactly_the_grants_the_entity_places_in_it(string state)
    {
        var (handler, grants) = Build(nameof(Each_state_filter_returns_exactly_the_grants_the_entity_places_in_it) + state);

        var result = await handler.HandleAsync(new ListExportGrantsQuery(State: FilterValue.Of(state)), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        var expected = grants[Enum.Parse<ExportGrantState>(state)];
        Assert.Equal(expected.Id, row.Id);
        Assert.Equal(state, row.State);
        Assert.Equal(expected.StateAt(Now).ToString(), row.State);
        Assert.Equal("ExportGrantState" + state, row.StateName);
    }

    /// <summary>With no filter every grant is listed, a spelling that names no state matches none, and the row type has no token member.</summary>
    [Fact]
    public async Task No_filter_lists_everything_an_unknown_state_lists_nothing_and_no_row_carries_a_token()
    {
        var (handler, _) = Build(nameof(No_filter_lists_everything_an_unknown_state_lists_nothing_and_no_row_carries_a_token));

        var all = await handler.HandleAsync(new ListExportGrantsQuery(), CancellationToken.None);
        var none = await handler.HandleAsync(new ListExportGrantsQuery(State: FilterValue.Of("Weird")), CancellationToken.None);

        Assert.Equal(4, all.Value!.Total);
        Assert.Equal(0, none.Value!.Total);
        Assert.DoesNotContain(
            typeof(Common.ExportGrantSummaryDto).GetProperties(),
            property =>
            {
                return property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Contains("Digest", StringComparison.OrdinalIgnoreCase);
            });
    }
}
