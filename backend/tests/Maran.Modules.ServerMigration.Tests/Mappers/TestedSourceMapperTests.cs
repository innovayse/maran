using Maran.Agent.Client.Services.TransferService;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Mappers;

namespace Maran.Modules.ServerMigration.Tests.Mappers;

/// <summary>What the agent's probes found becomes the snapshot the registry records, with an undetermined kind left undetermined.</summary>
public sealed class TestedSourceMapperTests
{
    /// <summary>Every fact is carried, and the servers are recorded by name.</summary>
    [Fact]
    public void Every_fact_is_carried()
    {
        var snapshot = TestedSourceMapper.ToSnapshot(new AgentTestedSource(
            IsRoot: true,
            HomeFreeBytes: 10,
            HomeTotalBytes: 100,
            SystemRelease: "AlmaLinux 9.4",
            Kind: SourceKind.Maran,
            MaranVersion: "1.2.3",
            DatabaseServer: SourceDatabaseServer.MySql,
            WebServer: SourceWebServer.Apache,
            MailFound: false));

        Assert.True(snapshot.IsRoot);
        Assert.Equal(10, snapshot.HomeFreeBytes);
        Assert.Equal(100, snapshot.HomeTotalBytes);
        Assert.Equal("AlmaLinux 9.4", snapshot.SystemRelease);
        Assert.Equal(RemoteSourceKind.Maran, snapshot.Kind);
        Assert.Equal("1.2.3", snapshot.MaranVersion);
        Assert.Equal("MySql", snapshot.DatabaseServer);
        Assert.Equal("Apache", snapshot.WebServer);
        Assert.False(snapshot.MailFound);
    }

    /// <summary>A kind the agent could not determine, and servers it did not find, stay null; a generic system is generic.</summary>
    [Fact]
    public void What_the_agent_could_not_tell_stays_null()
    {
        var undetermined = TestedSourceMapper.ToSnapshot(new AgentTestedSource(false, null, null, null, null, null, null, null, true));
        var generic = TestedSourceMapper.ToSnapshot(
            new AgentTestedSource(false, null, null, null, SourceKind.GenericLinux, null, SourceDatabaseServer.MariaDb, SourceWebServer.Nginx, true));

        Assert.Null(undetermined.Kind);
        Assert.Null(undetermined.DatabaseServer);
        Assert.Null(undetermined.WebServer);
        Assert.Equal(RemoteSourceKind.GenericLinux, generic.Kind);
        Assert.Equal("MariaDb", generic.DatabaseServer);
        Assert.Equal("Nginx", generic.WebServer);
    }
}
