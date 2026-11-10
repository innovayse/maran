using Maran.Agent.Client.Services.TransferService;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Mappers;

namespace Maran.Modules.ServerMigration.Tests.Mappers;

/// <summary>What the agent read from a source becomes the rows the registry stores, with unknown sizes kept unknown.</summary>
public sealed class DiscoveredInventoryMapperTests
{
    /// <summary>An account keeps its login, home, counts, sites and databases.</summary>
    [Fact]
    public void An_account_keeps_everything_the_agent_reported()
    {
        var serverId = Guid.NewGuid();
        var source = new AgentSourceAccount(
            "alice",
            "/home/alice",
            4_096,
            [
                new AgentSourceSite("alice.example", ["www.alice.example"], SourceSiteBackend.Proxy, null, "/srv/alice"),
                new AgentSourceSite("plain.example", [], SourceSiteBackend.Static, null, "/home/alice/plain"),
            ],
            [new AgentSourceDatabase("alice_db", 8_192)],
            3,
            9);

        var account = DiscoveredInventoryMapper.ToAccount(serverId, source);

        Assert.Equal(serverId, account.RemoteServerId);
        Assert.Equal("alice", account.Name);
        Assert.Equal("/home/alice", account.Home);
        Assert.True(account.DiskKnown);
        Assert.Equal(4_096, account.DiskBytes);
        Assert.Equal(3, account.CronEntryCount);
        Assert.Equal(9, account.MailboxCount);
        Assert.Equal(2, account.SiteCount);
        Assert.Equal(1, account.DatabaseCount);
        Assert.Equal("alice.example www.alice.example plain.example", account.DomainsText);
        Assert.Equal(DiscoveredSiteBackend.Proxy, account.Sites.First().Backend);
        Assert.Equal(8_192, account.Databases.Single().SizeBytes);
    }

    /// <summary>A size the source could not measure stays unknown and is not stored as a measured zero.</summary>
    [Fact]
    public void An_unmeasured_size_stays_unknown()
    {
        var source = new AgentSourceAccount(
            "bob", "/home/bob", null, [], [new AgentSourceDatabase("bob_db", null), new AgentSourceDatabase("empty_db", 0)], 0, 0);

        var account = DiscoveredInventoryMapper.ToAccount(Guid.NewGuid(), source);

        Assert.False(account.DiskKnown);
        Assert.Equal(0, account.DiskBytes);
        Assert.False(account.Databases.Single(database =>
        {
            return database.Name == "bob_db";
        }).SizeKnown);
        Assert.True(account.Databases.Single(database =>
        {
            return database.Name == "empty_db";
        }).SizeKnown);
    }

    /// <summary>Every kind of warning the agent client defines maps to the module's kind of the same name.</summary>
    [Fact]
    public void Every_warning_kind_maps_to_the_kind_of_the_same_name()
    {
        var serverId = Guid.NewGuid();

        foreach (var kind in Enum.GetValues<SourceWarningKind>())
        {
            var warning = DiscoveredInventoryMapper.ToWarning(serverId, new AgentSourceWarning(kind, "subject"));

            Assert.Equal(kind.ToString(), warning.Kind.ToString());
            Assert.Equal(serverId, warning.RemoteServerId);
            Assert.Equal("subject", warning.Subject);
        }
    }

    /// <summary>Every backend the agent client defines maps to the module's backend of the same name.</summary>
    [Fact]
    public void Every_backend_maps_to_the_backend_of_the_same_name()
    {
        foreach (var backend in Enum.GetValues<SourceSiteBackend>())
        {
            var account = DiscoveredInventoryMapper.ToAccount(
                Guid.NewGuid(),
                new AgentSourceAccount("u", "/home/u", 1, [new AgentSourceSite("d.example", [], backend, null, "/r")], [], 0, 0));

            Assert.Equal(backend.ToString(), account.Sites.Single().Backend.ToString());
        }
    }
}
