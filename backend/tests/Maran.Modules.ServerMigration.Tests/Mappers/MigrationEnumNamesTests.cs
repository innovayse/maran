using System.Globalization;
using System.Resources;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Tests.TestSupport;

namespace Maran.Modules.ServerMigration.Tests.Mappers;

/// <summary>
/// The module's closed vocabularies are named in all three languages: every member of every enum a response carries
/// has an entry, so a member added later without its words fails here and not on a customer's screen as a bare key.
/// </summary>
public sealed class MigrationEnumNamesTests
{
    /// <summary>The cultures this panel ships.</summary>
    private static readonly string[] Cultures = ["en", "ru", "hy"];

    /// <summary>The module's display-name table, read the way the panel's resolver reads it.</summary>
    private static readonly ResourceManager Names =
        new("Maran.Modules.ServerMigration.Resources.DisplayNames", typeof(DisplayNames).Assembly);

    /// <summary>The keys the mappers will ask for, for every member of every vocabulary.</summary>
    /// <returns>Every key.</returns>
    private static List<string> EveryKey()
    {
        return Enum.GetValues<RemoteServerState>().Select(MigrationEnumNames.KeyOf)
            .Concat(Enum.GetValues<RemoteSourceKind>().Select(MigrationEnumNames.KeyOf))
            .Concat(Enum.GetValues<RemoteFailureKind>().Select(MigrationEnumNames.KeyOf))
            .Concat(Enum.GetValues<DiscoveredSiteBackend>().Select(MigrationEnumNames.KeyOf))
            .Concat(Enum.GetValues<DiscoveryWarningKind>().Select(DiscoveryWarningMapper.KeyOf))
            .Concat(Enum.GetValues<MigrationJobState>().Select(MigrationEnumNames.KeyOf))
            .Concat(Enum.GetValues<MigrationAccountState>().Select(MigrationEnumNames.KeyOf))
            .Concat(Enum.GetValues<AccountImportState>().Select(MigrationEnumNames.KeyOf))
            .Concat(Enum.GetValues<ExportGrantState>().Select(MigrationEnumNames.KeyOf))
            .ToList();
    }

    /// <summary>The names are the enum's type name followed by the member's, which is what the table is keyed by.</summary>
    [Fact]
    public void A_name_is_the_type_name_followed_by_the_member()
    {
        Assert.Equal("RemoteServerStateReady", MigrationEnumNames.KeyOf(RemoteServerState.Ready));
        Assert.Equal("RemoteSourceKindMaran", MigrationEnumNames.KeyOf(RemoteSourceKind.Maran));
        Assert.Equal("RemoteFailureKindHostKeyChanged", MigrationEnumNames.KeyOf(RemoteFailureKind.HostKeyChanged));
        Assert.Equal("DiscoveredSiteBackendPhp", MigrationEnumNames.KeyOf(DiscoveredSiteBackend.Php));
        Assert.Equal("DiscoveryWarningTruncated", DiscoveryWarningMapper.KeyOf(DiscoveryWarningKind.Truncated));
        Assert.Equal("MigrationJobStateDraft", MigrationEnumNames.KeyOf(MigrationJobState.Draft));
        Assert.Equal("MigrationAccountStateCuttingOver", MigrationEnumNames.KeyOf(MigrationAccountState.CuttingOver));
        Assert.Equal("AccountImportStateStaged", MigrationEnumNames.KeyOf(AccountImportState.Staged));
        Assert.Equal("ExportGrantStateLive", MigrationEnumNames.KeyOf(ExportGrantState.Live));
    }

    /// <summary>Every member of every vocabulary has a name in English, Russian and Armenian.</summary>
    [Fact]
    public void Every_member_has_a_name_in_all_three_locales()
    {
        var keys = EveryKey();

        Assert.True(keys.Count >= 48, "The walk must see every member; it saw " + keys.Count);
        foreach (var key in keys)
        {
            foreach (var culture in Cultures)
            {
                var text = Names.GetString(key, CultureInfo.GetCultureInfo(culture));
                Assert.False(string.IsNullOrWhiteSpace(text), $"{key} has no {culture} entry.");
            }
        }
    }

    /// <summary>A key nobody defined reads as nothing, which is what makes the walk above able to fail.</summary>
    [Fact]
    public void A_key_no_table_defines_reads_as_absent()
    {
        Assert.Null(Names.GetString("RemoteServerStateNobodyDefined", CultureInfo.GetCultureInfo("ru")));
    }

    /// <summary>The mappers ask the lookup for exactly these keys.</summary>
    [Fact]
    public void The_mappers_ask_for_exactly_these_keys()
    {
        var names = new EchoDisplayNames();

        Assert.Equal("RemoteServerStatePinned", MigrationEnumNames.Of(names, RemoteServerState.Pinned));
        Assert.Equal("RemoteSourceKindMaran", MigrationEnumNames.Of(names, (RemoteSourceKind?)RemoteSourceKind.Maran));
        Assert.Null(MigrationEnumNames.Of(names, (RemoteSourceKind?)null));
        Assert.Equal("RemoteFailureKindUnreachable", MigrationEnumNames.Of(names, (RemoteFailureKind?)RemoteFailureKind.Unreachable));
        Assert.Null(MigrationEnumNames.Of(names, (RemoteFailureKind?)null));
        Assert.Equal("DiscoveredSiteBackendProxy", MigrationEnumNames.Of(names, DiscoveredSiteBackend.Proxy));
        Assert.Equal("MigrationJobStatePaused", MigrationEnumNames.Of(names, MigrationJobState.Paused));
        Assert.Equal("MigrationAccountStateSynced", MigrationEnumNames.Of(names, MigrationAccountState.Synced));
        Assert.Equal("AccountImportStateDiscarded", MigrationEnumNames.Of(names, AccountImportState.Discarded));
        Assert.Equal("ExportGrantStateExpired", MigrationEnumNames.Of(names, ExportGrantState.Expired));
    }
}
