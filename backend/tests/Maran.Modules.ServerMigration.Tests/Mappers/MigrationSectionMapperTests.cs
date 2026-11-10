using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Models;
using Maran.Sdk.Contracts;

namespace Maran.Modules.ServerMigration.Tests.Mappers;

/// <summary>Restating a discovered account as the sections the modules' importers read (issue #197, phase 2).</summary>
public sealed class MigrationSectionMapperTests
{
    /// <summary>An account with a PHP site, a proxy site, one importable database and one that is not.</summary>
    /// <param name="cron">How many crontab entries it has.</param>
    /// <param name="mailboxes">How many mailboxes it has.</param>
    /// <returns>The account, mapped.</returns>
    private static MigrationJobAccount Account(int cron = 0, int mailboxes = 0)
    {
        var inventory = new MigrationInventorySnapshot(
            [
                new MigrationSnapshotSite("alice.example.test", ["www.alice.example.test"], DiscoveredSiteBackend.Php, "8.3"),
                new MigrationSnapshotSite("proxy.example.test", [], DiscoveredSiteBackend.Proxy, null),
            ],
            [new MigrationSnapshotDatabase("alice_shop", 10), new MigrationSnapshotDatabase("legacy", 10)]);
        var account = new MigrationJobAccount(
            Guid.NewGuid(), Guid.NewGuid(), "alice", "/home/alice", 100, cron, mailboxes,
            MigrationSectionMapper.WriteInventory(inventory), "alice.example.test");
        account.Remap(Guid.NewGuid(), null, false, "alice@example.test", "en", null, null);

        return account;
    }

    /// <summary>The account section carries the mapped owner and the domain, never a plan name or a secret.</summary>
    [Fact]
    public void The_accounts_section_carries_the_mapping()
    {
        var composition = MigrationSectionMapper.Compose(Account());

        var section = composition.Sections.Single(row =>
        {
            return row.ModuleId == "accounts";
        });
        Assert.Contains("\"ownerEmail\":\"alice@example.test\"", section.Json);
        Assert.Contains("\"primaryDomain\":\"alice.example.test\"", section.Json);
        Assert.Contains("\"name\":\"alice\"", section.Json);
    }

    /// <summary>A proxy site is reported and left out; a PHP site is a PHP backend (2) with its version.</summary>
    [Fact]
    public void A_proxy_site_is_left_out_and_reported_and_a_php_site_keeps_its_version()
    {
        var composition = MigrationSectionMapper.Compose(Account());

        var sites = composition.Sections.Single(row =>
        {
            return row.ModuleId == "sites";
        });
        Assert.Equal(1, sites.ResourceCount);
        Assert.Contains("\"backendType\":2", sites.Json);
        Assert.Contains("\"phpVersion\":\"8.3\"", sites.Json);
        Assert.DoesNotContain("proxy.example.test", sites.Json);
        Assert.Contains(composition.Findings, finding =>
        {
            return finding.Code == "MigrationSiteProxyNotImported" && finding.Subject == "proxy.example.test";
        });
    }

    /// <summary>A database that cannot keep its name is a conflict and is not in the section; one that can, is.</summary>
    [Fact]
    public void A_database_that_cannot_keep_its_name_is_a_conflict_and_left_out()
    {
        var composition = MigrationSectionMapper.Compose(Account());

        var databases = composition.Sections.Single(row =>
        {
            return row.ModuleId == "databases";
        });
        Assert.Equal(1, databases.ResourceCount);
        Assert.Contains("\"name\":\"shop\"", databases.Json);
        Assert.DoesNotContain("legacy", databases.Json);
        var conflict = Assert.Single(composition.Findings, finding =>
        {
            return finding.Code == "MigrationDatabaseNameNotImportable";
        });
        Assert.Equal(MigrationFindingSeverity.Conflict, conflict.Severity);
        Assert.Equal("legacy", conflict.Subject);
    }

    /// <summary>Cron entries and mailboxes are counted by the discovery, so they are reported and not carried.</summary>
    [Fact]
    public void Cron_and_mail_are_warnings_when_present_and_silent_when_absent()
    {
        var with = MigrationSectionMapper.Compose(Account(cron: 3, mailboxes: 2));
        var without = MigrationSectionMapper.Compose(Account());

        Assert.Contains(with.Findings, finding =>
        {
            return finding.Code == "MigrationCronNotMigrated" && finding.Severity == MigrationFindingSeverity.Warning;
        });
        Assert.Contains(with.Findings, finding =>
        {
            return finding.Code == "MigrationMailNotMigrated";
        });
        Assert.DoesNotContain(without.Findings, finding =>
        {
            return finding.Code is "MigrationCronNotMigrated" or "MigrationMailNotMigrated";
        });
        Assert.DoesNotContain(with.Sections, row =>
        {
            return row.ModuleId is "cron" or "mail";
        });
    }

    /// <summary>The snapshot survives the round trip through the text the job stores.</summary>
    [Fact]
    public void The_snapshot_round_trips()
    {
        var read = MigrationSectionMapper.ReadInventory(Account());

        Assert.Equal(2, read.Sites.Count);
        Assert.Equal(DiscoveredSiteBackend.Php, read.Sites[0].Backend);
        Assert.Equal(["www.alice.example.test"], read.Sites[0].Aliases);
        Assert.Equal(2, read.Databases.Count);
    }
}
