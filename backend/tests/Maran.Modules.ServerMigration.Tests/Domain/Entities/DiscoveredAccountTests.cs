using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;

namespace Maran.Modules.ServerMigration.Tests.Domain.Entities;

/// <summary>
/// What a discovered account keeps in step with its children, and how it treats text a source reported: the
/// counts and the search text are always the children's, an unknown size stays unknown, and an over-long name is
/// shortened instead of costing the discovery.
/// </summary>
public sealed class DiscoveredAccountTests
{
    /// <summary>An account with no children has zero counts and nothing to search.</summary>
    [Fact]
    public void A_new_account_has_no_sites_databases_or_search_text()
    {
        var account = Account();

        Assert.Equal(0, account.SiteCount);
        Assert.Equal(0, account.DatabaseCount);
        Assert.Equal(string.Empty, account.DomainsText);
        Assert.Empty(account.Sites);
        Assert.Empty(account.Databases);
        Assert.Equal(4, account.CronEntryCount);
        Assert.Equal(2, account.MailboxCount);
        Assert.True(account.DiskKnown);
        Assert.Equal(1234, account.DiskBytes);
    }

    /// <summary>Adding a site moves the count and puts the domain and its aliases in the search text.</summary>
    [Fact]
    public void Adding_sites_keeps_the_count_and_the_search_text_in_step()
    {
        var account = Account();

        account.AddSite(Guid.NewGuid(), "a.example", ["www.a.example", "alias.a.example"], DiscoveredSiteBackend.Php, "8.2", "/home/u/a");
        account.AddSite(Guid.NewGuid(), "b.example", [], DiscoveredSiteBackend.Static, null, "/home/u/b");

        Assert.Equal(2, account.SiteCount);
        Assert.Equal(2, account.Sites.Count);
        Assert.Equal("a.example www.a.example alias.a.example b.example", account.DomainsText);
        var first = account.Sites.First();
        Assert.Equal("www.a.example alias.a.example", first.AliasesText);
        Assert.Equal(account.Id, first.DiscoveredAccountId);
        Assert.Equal("8.2", first.PhpVersion);
        Assert.Equal(string.Empty, account.Sites.Last().AliasesText);
        Assert.Null(account.Sites.Last().PhpVersion);
    }

    /// <summary>Adding a database moves the count, and a size the source could not measure stays unknown.</summary>
    [Fact]
    public void Adding_databases_keeps_the_count_and_an_unmeasured_size_unknown()
    {
        var account = Account();

        account.AddDatabase(Guid.NewGuid(), "shop", 4096);
        account.AddDatabase(Guid.NewGuid(), "blog", null);

        Assert.Equal(2, account.DatabaseCount);
        var shop = account.Databases.Single(database => { return database.Name == "shop"; });
        var blog = account.Databases.Single(database => { return database.Name == "blog"; });
        Assert.True(shop.SizeKnown);
        Assert.Equal(4096, shop.SizeBytes);
        Assert.False(blog.SizeKnown);
        Assert.Equal(0, blog.SizeBytes);
    }

    /// <summary>A disk figure the source could not measure is unknown, not zero.</summary>
    [Fact]
    public void An_unmeasured_disk_is_unknown_and_not_a_measured_zero()
    {
        var unknown = new DiscoveredAccount(Guid.NewGuid(), Guid.NewGuid(), "u", "/home/u", null, 0, 0);
        var empty = new DiscoveredAccount(Guid.NewGuid(), Guid.NewGuid(), "u", "/home/u", 0, 0, 0);

        Assert.False(unknown.DiskKnown);
        Assert.True(empty.DiskKnown);
        Assert.Equal(0, unknown.DiskBytes);
        Assert.Equal(0, empty.DiskBytes);
    }

    /// <summary>Negative figures are a caller bug and are refused.</summary>
    [Fact]
    public void Negative_figures_are_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => { return new DiscoveredAccount(Guid.NewGuid(), Guid.NewGuid(), "u", "/h", -1, 0, 0); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { return new DiscoveredAccount(Guid.NewGuid(), Guid.NewGuid(), "u", "/h", 0, -1, 0); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { return new DiscoveredAccount(Guid.NewGuid(), Guid.NewGuid(), "u", "/h", 0, 0, -1); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { Account().AddDatabase(Guid.NewGuid(), "d", -5); });
    }

    /// <summary>Text the source reported is shortened to its column, so one over-long name cannot lose the discovery.</summary>
    [Fact]
    public void Over_long_text_from_the_source_is_shortened_to_its_column()
    {
        var account = new DiscoveredAccount(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new string('n', 100),
            "/home/" + new string('h', 5000),
            1,
            0,
            0);
        account.AddSite(Guid.NewGuid(), new string('d', 400), [], DiscoveredSiteBackend.Unknown, new string('9', 40), new string('r', 5000));
        account.AddDatabase(Guid.NewGuid(), new string('b', 200), 1);

        Assert.Equal(DiscoveredAccount.NameMaxLength, account.Name.Length);
        Assert.Equal(DiscoveredAccount.HomeMaxLength, account.Home.Length);
        var site = Assert.Single(account.Sites);
        Assert.Equal(DiscoveredSite.DomainMaxLength, site.Domain.Length);
        Assert.Equal(DiscoveredSite.PhpVersionMaxLength, site.PhpVersion!.Length);
        Assert.Equal(DiscoveredSite.DocumentRootMaxLength, site.DocumentRoot.Length);
        Assert.Equal(DiscoveredDatabase.NameMaxLength, Assert.Single(account.Databases).Name.Length);
    }

    /// <summary>Control: text that fits is kept exactly as reported.</summary>
    [Fact]
    public void Text_that_fits_is_kept_exactly_as_reported()
    {
        var account = Account();
        account.AddSite(Guid.NewGuid(), "a.example", [], DiscoveredSiteBackend.Proxy, null, "/srv/a");

        Assert.Equal("shop-user", account.Name);
        Assert.Equal("/home/shop-user", account.Home);
        Assert.Equal("a.example", Assert.Single(account.Sites).Domain);
    }

    /// <summary>A warning keeps its kind and shortens a subject that does not fit.</summary>
    [Fact]
    public void A_warning_keeps_its_kind_and_shortens_an_over_long_subject()
    {
        var warning = new DiscoveryWarning(Guid.NewGuid(), Guid.NewGuid(), DiscoveryWarningKind.NameRefused, new string('s', 1000));
        var plain = new DiscoveryWarning(Guid.NewGuid(), Guid.NewGuid(), DiscoveryWarningKind.Truncated, string.Empty);

        Assert.Equal(DiscoveryWarningKind.NameRefused, warning.Kind);
        Assert.Equal(DiscoveryWarning.SubjectMaxLength, warning.Subject.Length);
        Assert.Equal(string.Empty, plain.Subject);
        Assert.Throws<ArgumentNullException>(() => { return new DiscoveryWarning(Guid.NewGuid(), Guid.NewGuid(), DiscoveryWarningKind.Truncated, null!); });
    }

    /// <summary>A host key candidate that cannot be stored is refused when it is made.</summary>
    [Fact]
    public void A_host_key_candidate_over_a_column_width_is_refused_when_offered()
    {
        var server = new RemoteServer(Guid.NewGuid(), "n", "h.example", 22, "u", null, "ssh-ed25519 AAAA", "k", DateTimeOffset.UnixEpoch);

        Assert.ThrowsAny<ArgumentException>(() => { server.OfferHostKeys([("a", "k", new string('f', RemoteServer.FingerprintLength + 1))]); });
        Assert.ThrowsAny<ArgumentException>(() => { server.OfferHostKeys([(new string('a', 40), "k", "SHA256:x")]); });
        server.OfferHostKeys([("ssh-ed25519", "k", "SHA256:x")]);
        Assert.Single(server.HostKeyCandidates);
    }

    /// <summary>An account with the fields most tests need.</summary>
    /// <returns>The account.</returns>
    private static DiscoveredAccount Account()
    {
        return new DiscoveredAccount(Guid.NewGuid(), Guid.NewGuid(), "shop-user", "/home/shop-user", 1234, 4, 2);
    }
}
