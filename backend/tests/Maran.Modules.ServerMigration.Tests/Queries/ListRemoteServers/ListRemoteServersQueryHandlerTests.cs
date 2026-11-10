using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Queries.ListRemoteServers;
using Maran.Modules.ServerMigration.Tests.TestSupport;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.ServerMigration.Tests.Queries.ListRemoteServers;

/// <summary>
/// Listing remote servers: order, paging with the true total, the state filter's exact match, and the wire shape's
/// state names. The ILIKE search is a PostgreSQL function the in-memory provider cannot evaluate, so it is exercised
/// by the HTTP-level suite and not here.
/// </summary>
public sealed class ListRemoteServersQueryHandlerTests
{
    /// <summary>Builds a registered server.</summary>
    /// <param name="name">The label.</param>
    /// <param name="host">The address.</param>
    /// <returns>The server, awaiting confirmation of its host key.</returns>
    private static RemoteServer Make(string name, string host)
    {
        var server = new RemoteServer(
            Guid.NewGuid(), name, host, 22, "maranmig", null, "ssh-ed25519 AAAAkey", "private", RemoteServerTestValues.CreatedAt);
        server.OfferHostKeys([("ssh-ed25519", "AAAAed", RemoteServerTestValues.FingerprintA)]);
        return server;
    }

    /// <summary>Seeds three servers in three states: unconfirmed, ready and unreachable.</summary>
    /// <param name="world">The test's world.</param>
    /// <returns>A task that completes when they are saved.</returns>
    private static async Task SeedThreeAsync(MigrationHandlerWorld world)
    {
        var alpha = Make("alpha", "alpha.example.net");

        var bravo = Make("bravo", "bravo.example.net");
        bravo.PinHostKey(RemoteServerTestValues.FingerprintA, RemoteServerTestValues.LaterAt);
        bravo.RecordTestSucceeded(RemoteServerTestValues.Snapshot(), RemoteServerTestValues.LaterAt);

        var charlie = Make("charlie", "charlie.example.net");
        charlie.PinHostKey(RemoteServerTestValues.FingerprintA, RemoteServerTestValues.LaterAt);
        charlie.RecordTestFailed(RemoteFailureKind.Unreachable, RemoteServerTestValues.CreatedAt);

        await world.SeedAsync(alpha);
        await world.SeedAsync(bravo);
        await world.SeedAsync(charlie);
    }

    /// <summary>The list is by name when no sort is named, and a named sort reverses it; the total counts every server.</summary>
    [Fact]
    public async Task The_list_is_by_name_and_a_named_sort_reorders_it()
    {
        using var world = new MigrationHandlerWorld();
        await SeedThreeAsync(world);

        var byName = await world.List.HandleAsync(new ListRemoteServersQuery(), CancellationToken.None);
        var newestTestFirst = await world.List.HandleAsync(
            new ListRemoteServersQuery(Sort: SortRequest.Parse("-lastTestedAt")), CancellationToken.None);
        var descending = await world.List.HandleAsync(
            new ListRemoteServersQuery(Sort: SortRequest.Parse("-name")), CancellationToken.None);

        Assert.Equal(3, byName.Value.Total);
        Assert.Equal(["alpha", "bravo", "charlie"], byName.Value.Items.Select(row =>
        {
            return row.Name;
        }));
        Assert.Equal("bravo", newestTestFirst.Value.Items[0].Name);
        Assert.Equal(["charlie", "bravo", "alpha"], descending.Value.Items.Select(row =>
        {
            return row.Name;
        }));
    }

    /// <summary>A page answers its rows with the true total, and a page past the end is empty with that total.</summary>
    [Fact]
    public async Task A_page_carries_the_true_total_and_a_page_past_the_end_is_empty()
    {
        using var world = new MigrationHandlerWorld();
        await SeedThreeAsync(world);

        // The smallest size the list offers is ten, so a second page needs eleven servers.
        for (var index = 0; index < 8; index++)
        {
            await world.SeedAsync(Make($"delta{index}", $"delta{index}.example.net"));
        }

        var first = await world.List.HandleAsync(new ListRemoteServersQuery(Page: 1, PageSize: 10), CancellationToken.None);
        var second = await world.List.HandleAsync(new ListRemoteServersQuery(Page: 2, PageSize: 10), CancellationToken.None);
        var past = await world.List.HandleAsync(new ListRemoteServersQuery(Page: 9, PageSize: 10), CancellationToken.None);

        Assert.Equal(10, first.Value.Items.Count);
        Assert.Equal(11, second.Value.Total);
        Assert.Equal("delta7", Assert.Single(second.Value.Items).Name);
        Assert.Empty(past.Value.Items);
        Assert.Equal(11, past.Value.Total);
        Assert.Equal(9, past.Value.Page);
    }

    /// <summary>The state filter is an exact match on the state's name; a different case, a number or an unknown word matches nothing.</summary>
    [Fact]
    public async Task The_state_filter_is_an_exact_match_on_the_name()
    {
        using var world = new MigrationHandlerWorld();
        await SeedThreeAsync(world);

        var ready = await world.List.HandleAsync(new ListRemoteServersQuery(State: FilterValue.Of("Ready")), CancellationToken.None);
        var unreachable = await world.List.HandleAsync(
            new ListRemoteServersQuery(State: FilterValue.Of("Unreachable")), CancellationToken.None);

        Assert.Equal("bravo", Assert.Single(ready.Value.Items).Name);
        Assert.Equal("charlie", Assert.Single(unreachable.Value.Items).Name);
        foreach (var spelling in new[] { "ready", "READY", "2", "Nonsense" })
        {
            var none = await world.List.HandleAsync(
                new ListRemoteServersQuery(State: FilterValue.Of(spelling)), CancellationToken.None);
            Assert.Equal(0, none.Value.Total);
        }
    }

    /// <summary>A row names its state and kind in the caller's language and carries the pinned fingerprint, and nothing of a key.</summary>
    [Fact]
    public async Task A_row_names_its_state_and_carries_no_key_material()
    {
        using var world = new MigrationHandlerWorld();
        await SeedThreeAsync(world);

        var result = await world.List.HandleAsync(
            new ListRemoteServersQuery(State: FilterValue.Of("Ready")), CancellationToken.None);

        var row = Assert.Single(result.Value.Items);
        Assert.Equal(RemoteServerState.Ready, row.State);
        Assert.Equal("RemoteServerStateReady", row.StateName);
        Assert.Equal(RemoteSourceKind.GenericLinux, row.Kind);
        Assert.Equal("RemoteSourceKindGenericLinux", row.KindName);
        Assert.Equal(RemoteServerTestValues.FingerprintA, row.HostKeyFingerprint);
        Assert.DoesNotContain("private", System.Text.Json.JsonSerializer.Serialize(row), StringComparison.Ordinal);
    }
}
