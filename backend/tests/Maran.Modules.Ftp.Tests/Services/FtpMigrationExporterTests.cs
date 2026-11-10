using System.Text.Json;
using Maran.Modules.Ftp.Domain.Entities;
using Maran.Modules.Ftp.Services;
using Maran.Modules.Ftp.Tests.TestSupport;

namespace Maran.Modules.Ftp.Tests.Services;

/// <summary>
/// What the FTPS section of an account move holds, and that no secret planted in a login row ever reaches it
/// (issue #127; rules/security.md item 8).
/// </summary>
public sealed class FtpMigrationExporterTests
{
    /// <summary>The prefix of every value planted in a column the section does not name.</summary>
    private const string Planted = "PLANTED-SECRET-";

    /// <summary>Plants a recognisable value in every string column the section does not carry (everything but the login name).</summary>
    /// <param name="user">The login.</param>
    /// <returns>The planted values, by property name.</returns>
    private static Dictionary<string, string> PlantInEveryOtherStringColumn(FtpUser user)
    {
        var planted = new Dictionary<string, string>();

        foreach (var property in typeof(FtpUser).GetProperties().Where(p => { return p.PropertyType == typeof(string) && p.Name != "Name"; }))
        {
            planted[property.Name] = Planted + property.Name;
            property.SetValue(user, planted[property.Name]);
        }

        return planted;
    }

    /// <summary>The section names the login and holds none of the planted values.</summary>
    [Fact]
    public async Task A_secret_planted_in_a_column_the_section_does_not_name_never_appears_in_it()
    {
        var account = Guid.NewGuid();
        using var world = new FtpTestContext();
        var user = new FtpUser(Guid.NewGuid(), account, "uploader", "acct_uploader", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var planted = PlantInEveryOtherStringColumn(user);
        world.Database.FtpUsers.Add(user);
        await world.Database.SaveChangesAsync();

        var section = await new FtpMigrationExporter(world.Database).ExportAsync(account, CancellationToken.None);

        Assert.NotEmpty(planted);
        Assert.NotNull(section);
        Assert.Contains("uploader", section.Json, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, section.Json, StringComparison.Ordinal);
    }

    /// <summary>The positive control: serialising the row itself DOES carry the planted values.</summary>
    [Fact]
    public void The_probe_can_see_a_planted_value_in_a_serialised_row()
    {
        var user = new FtpUser(Guid.NewGuid(), Guid.NewGuid(), "uploader", "acct_uploader", DateTimeOffset.UnixEpoch);
        var planted = PlantInEveryOtherStringColumn(user);

        var json = JsonSerializer.Serialize(user, JsonSerializerOptions.Web);

        Assert.NotEmpty(planted);
        Assert.All(planted.Values, value => { Assert.Contains(value, json, StringComparison.Ordinal); });
    }
}
