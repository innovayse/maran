using Maran.Modules.Fleet.Domain.Enums;
using Maran.Modules.Fleet.Seeders;
using Maran.Modules.Fleet.Tests.TestSupport;
using Maran.SharedKernel.Constants;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Fleet.Tests.Seeders;

/// <summary>
/// What the local-server seeder promises a panel: after a start, the server it runs on is in the registry
/// exactly once, whatever the panel did before.
/// </summary>
public sealed class LocalServerSeederTests
{
    /// <summary>A panel with an empty registry gets the local server, online, under the well-known id.</summary>
    [Fact]
    public async Task An_empty_registry_gets_the_local_server()
    {
        var name = nameof(An_empty_registry_gets_the_local_server);
        var clock = new FixedClock();
        await using var context = InMemoryFleetDatabase.Open(name);

        var inserted = await new LocalServerSeeder(context, clock).SeedAsync("panel-host", CancellationToken.None);

        Assert.True(inserted);
        var local = await context.Servers.SingleAsync();
        Assert.Equal(FleetServers.LocalId, local.Id);
        Assert.Equal("panel-host", local.Name);
        Assert.Equal(ServerKind.Local, local.Kind);
        Assert.Equal(ServerState.Online, local.State);
        Assert.Equal(clock.UtcNow, local.CreatedAt);
    }

    /// <summary>Seeding again inserts nothing: one local server stays one, after any number of starts.</summary>
    [Fact]
    public async Task Seeding_again_inserts_nothing()
    {
        var name = nameof(Seeding_again_inserts_nothing);
        await using var context = InMemoryFleetDatabase.Open(name);
        var seeder = new LocalServerSeeder(context, new FixedClock());
        await seeder.SeedAsync("panel-host", CancellationToken.None);

        var second = await seeder.SeedAsync("panel-host", CancellationToken.None);

        Assert.False(second);
        Assert.Equal(1, await context.Servers.CountAsync());
    }

    /// <summary>An administrator's rename of the local server survives the next start; the seed name is only a new row's.</summary>
    [Fact]
    public async Task A_renamed_local_server_keeps_its_name_across_a_restart()
    {
        var name = nameof(A_renamed_local_server_keeps_its_name_across_a_restart);
        await using (var first = InMemoryFleetDatabase.Open(name))
        {
            await new LocalServerSeeder(first, new FixedClock()).SeedAsync("panel-host", CancellationToken.None);
            var local = await first.Servers.SingleAsync();
            local.Rename("primary");
            await first.SaveChangesAsync();
        }

        await using var second = InMemoryFleetDatabase.Open(name);
        var inserted = await new LocalServerSeeder(second, new FixedClock())
            .SeedAsync("panel-host", CancellationToken.None);

        Assert.False(inserted);
        Assert.Equal("primary", (await second.Servers.SingleAsync()).Name);
    }

    /// <summary>The seeder leaves enrolled nodes alone and still adds the local server beside them.</summary>
    [Fact]
    public async Task Existing_nodes_are_not_touched_and_the_local_server_is_added_beside_them()
    {
        var name = nameof(Existing_nodes_are_not_touched_and_the_local_server_is_added_beside_them);
        await using var context = InMemoryFleetDatabase.Open(name);
        var node = ServerTestValues.PendingNode();
        context.Servers.Add(node);
        await context.SaveChangesAsync();

        var inserted = await new LocalServerSeeder(context, new FixedClock())
            .SeedAsync("panel-host", CancellationToken.None);

        Assert.True(inserted);
        Assert.Equal(2, await context.Servers.CountAsync());
        Assert.Equal(ServerState.Pending, (await context.Servers.SingleAsync(server => server.Id == node.Id)).State);
    }

    /// <summary>A blank name for a new row is a caller bug and stores nothing.</summary>
    [Fact]
    public async Task A_blank_name_stores_nothing()
    {
        var name = nameof(A_blank_name_stores_nothing);
        await using var context = InMemoryFleetDatabase.Open(name);

        await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
        {
            await new LocalServerSeeder(context, new FixedClock()).SeedAsync(" ", CancellationToken.None);
        });

        Assert.Equal(0, await context.Servers.CountAsync());
    }
}
