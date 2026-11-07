using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Sites.Tests.Services;

/// <summary>
/// The sites personal-data export: the allow-listed fields of the named account's sites and host names,
/// nobody else's, and no field a secret could ride in.
/// </summary>
public sealed class SitesPersonalDataProviderTests
{
    /// <summary>Fragments no exported field name may contain: the vocabulary of secrets and of output text.</summary>
    private static readonly string[] ForbiddenFragments =
    [
        "password", "hash", "secret", "token", "privatekey", "output", "stdout", "stderr", "log", "sha256", "digest",
    ];

    /// <summary>The fields a site record may carry, and no others.</summary>
    private static readonly string[] SiteFields = ["id", "domain", "status", "createdAt"];

    /// <summary>The fields a host name record may carry, and no others.</summary>
    private static readonly string[] HostnameFields = ["name", "siteId"];

    /// <summary>The export holds exactly the allow-listed fields of the named account's sites and host names.</summary>
    [Fact]
    public async Task The_export_holds_exactly_the_allow_listed_fields_of_the_named_accounts_sites_and_hostnames()
    {
        var mine = Guid.NewGuid();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin());
        context.Sites.Add(SitesTestContext.PhpSite(mine, "one.example.com", "8.3", "www.one.example.com"));
        context.Sites.Add(SitesTestContext.PhpSite(Guid.NewGuid(), "theirs.example.com"));
        await context.SaveChangesAsync();

        var sections = await new SitesPersonalDataProvider(context)
            .ExportAsync(new PersonalDataSubject(PersonalDataSubjectKind.Account, mine), CancellationToken.None);

        var sites = Assert.Single(sections, section => { return section.Name == "sites"; });
        var site = Assert.Single(sites.Records);
        Assert.Equal(SiteFields.Order(StringComparer.Ordinal), site.Fields.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("one.example.com", site.Fields["domain"]);

        var hostnames = Assert.Single(sections, section => { return section.Name == "hostnames"; });
        Assert.Equal(2, hostnames.Records.Count);
        Assert.All(hostnames.Records, record =>
        {
            Assert.Equal(
                HostnameFields.Order(StringComparer.Ordinal),
                record.Fields.Keys.Order(StringComparer.Ordinal));
        });

        var names = sections.SelectMany(section => { return section.Records; })
            .SelectMany(record => { return record.Fields.Keys; })
            .ToList();
        Assert.NotEmpty(names);
        Assert.All(names, name =>
        {
            Assert.DoesNotContain(
                ForbiddenFragments,
                fragment => { return name.Contains(fragment, StringComparison.OrdinalIgnoreCase); });
        });
    }

    /// <summary>A user subject gets an empty list, because this module holds no row about a login.</summary>
    [Fact]
    public async Task A_user_subject_gets_an_empty_list()
    {
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin());

        var sections = await new SitesPersonalDataProvider(context)
            .ExportAsync(new PersonalDataSubject(PersonalDataSubjectKind.User, Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(sections);
    }
}
