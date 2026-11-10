using System.Text.Json;
using System.Text.Json.Serialization;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;

namespace Maran.Modules.Sites.Tests.Services;

/// <summary>
/// What the Sites section of an account move holds, and that no secret planted in a site row ever reaches it
/// (issue #127; rules/security.md item 8).
/// </summary>
public sealed class SitesMigrationExporterTests
{
    /// <summary>The prefix of every value planted in a column the section does not name.</summary>
    private const string Planted = "PLANTED-SECRET-";

    /// <summary>The string columns the section carries, by property name: everything else must stay behind.</summary>
    private static readonly HashSet<string> Carried = ["Domain", "PhpVersion", "ProxyUpstream", "NodeVersion", "StartCommand"];

    /// <summary>
    /// How the control serialises a row: the entity graph points back at itself (a hostname holds its site), which
    /// the default options refuse, and the control only needs every string column of the row to be written once.
    /// </summary>
    private static readonly JsonSerializerOptions RowSerialisation =
        new(JsonSerializerOptions.Web) { ReferenceHandler = ReferenceHandler.IgnoreCycles };

    /// <summary>Plants a recognisable value in every string column the section does not carry.</summary>
    /// <param name="site">The site.</param>
    /// <returns>The planted values, by property name.</returns>
    private static Dictionary<string, string> PlantInEveryOtherStringColumn(Site site)
    {
        var planted = new Dictionary<string, string>();

        foreach (var property in typeof(Site).GetProperties().Where(p => { return p.PropertyType == typeof(string) && !Carried.Contains(p.Name); }))
        {
            planted[property.Name] = Planted + property.Name;
            property.SetValue(site, planted[property.Name]);
        }

        return planted;
    }

    /// <summary>The section holds the carried fields and none of the planted values, whichever column they sit in.</summary>
    [Fact]
    public async Task A_secret_planted_in_a_column_the_section_does_not_name_never_appears_in_it()
    {
        var account = Guid.NewGuid();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin());
        var site = SitesTestContext.PhpSite(account, "shop.example", "8.3", "www.shop.example");
        var planted = PlantInEveryOtherStringColumn(site);
        context.Sites.Add(site);
        await context.SaveChangesAsync();

        var section = await new SitesMigrationExporter(context).ExportAsync(account, CancellationToken.None);

        Assert.NotEmpty(planted);
        Assert.NotNull(section);
        Assert.Contains("shop.example", section.Json, StringComparison.Ordinal);
        Assert.Contains("8.3", section.Json, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, section.Json, StringComparison.Ordinal);
    }

    /// <summary>The positive control: serialising the row itself DOES carry the planted values, so the probe above can see a leak.</summary>
    [Fact]
    public void The_probe_can_see_a_planted_value_in_a_serialised_row()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "shop.example");
        var planted = PlantInEveryOtherStringColumn(site);

        var json = JsonSerializer.Serialize(site, RowSerialisation);

        Assert.NotEmpty(planted);
        Assert.All(planted.Values, value => { Assert.Contains(value, json, StringComparison.Ordinal); });
    }

    /// <summary>Another account's sites are not in the section.</summary>
    [Fact]
    public async Task Another_accounts_site_is_not_exported()
    {
        var mine = Guid.NewGuid();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin());
        context.Sites.Add(SitesTestContext.PhpSite(mine, "mine.example"));
        context.Sites.Add(SitesTestContext.PhpSite(Guid.NewGuid(), "theirs.example"));
        await context.SaveChangesAsync();

        var section = await new SitesMigrationExporter(context).ExportAsync(mine, CancellationToken.None);

        Assert.Contains("mine.example", section!.Json, StringComparison.Ordinal);
        Assert.DoesNotContain("theirs.example", section.Json, StringComparison.Ordinal);
    }
}
