using System.Text.Json;
using Maran.Modules.Sftp.Domain.Entities;
using Maran.Modules.Sftp.Services;
using Maran.Modules.Sftp.Tests.TestSupport;

namespace Maran.Modules.Sftp.Tests.Services;

/// <summary>
/// What the SFTP section of an account move holds, and that no secret planted in a login row ever reaches it
/// (issue #127; rules/security.md item 8).
/// </summary>
public sealed class SftpMigrationExporterTests
{
    /// <summary>The prefix of every value planted in a column the section does not name.</summary>
    private const string Planted = "PLANTED-SECRET-";

    /// <summary>The string columns the section carries.</summary>
    private static readonly HashSet<string> Carried = ["Name"];

    /// <summary>Plants a recognisable value in every string column the section does not carry.</summary>
    /// <param name="user">The login.</param>
    /// <returns>The planted values, by property name.</returns>
    private static Dictionary<string, string> PlantInEveryOtherStringColumn(SftpUser user)
    {
        var planted = new Dictionary<string, string>();

        foreach (var property in typeof(SftpUser).GetProperties().Where(p => { return p.PropertyType == typeof(string) && !Carried.Contains(p.Name); }))
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
        await using var context = SftpTestContext.Create(FakeCurrentUser.Admin());
        var user = SftpTestContext.Row(account, "acct", "deploy");
        var planted = PlantInEveryOtherStringColumn(user);
        context.SftpUsers.Add(user);
        await context.SaveChangesAsync();

        var section = await new SftpMigrationExporter(context).ExportAsync(account, CancellationToken.None);

        Assert.NotEmpty(planted);
        Assert.NotNull(section);
        Assert.Contains("deploy", section.Json, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, section.Json, StringComparison.Ordinal);
    }

    /// <summary>The positive control: serialising the row itself DOES carry the planted values.</summary>
    [Fact]
    public void The_probe_can_see_a_planted_value_in_a_serialised_row()
    {
        var user = SftpTestContext.Row(Guid.NewGuid(), "acct", "deploy");
        var planted = PlantInEveryOtherStringColumn(user);

        var json = JsonSerializer.Serialize(user, JsonSerializerOptions.Web);

        Assert.NotEmpty(planted);
        Assert.All(planted.Values, value => { Assert.Contains(value, json, StringComparison.Ordinal); });
    }
}
