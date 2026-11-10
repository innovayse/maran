using System.Text.Json;
using Maran.Modules.Databases.Domain.Entities;
using Maran.Modules.Databases.Domain.Enums;
using Maran.Modules.Databases.Services;
using Maran.Modules.Databases.Tests.TestSupport;

namespace Maran.Modules.Databases.Tests.Services;

/// <summary>
/// What the Databases section of an account move holds: the databases, the additional users and their grants,
/// and that no secret planted in any row ever reaches it (issue #127; rules/security.md item 8).
/// </summary>
public sealed class DatabasesMigrationExporterTests
{
    /// <summary>The prefix of every value planted in a column the section does not name.</summary>
    private const string Planted = "PLANTED-SECRET-";

    /// <summary>Plants a recognisable value in every string column of a row the section does not carry.</summary>
    /// <typeparam name="T">The row's type.</typeparam>
    /// <param name="row">The row.</param>
    /// <param name="carried">The property names the section carries.</param>
    /// <returns>The planted values, by property name.</returns>
    private static Dictionary<string, string> Plant<T>(T row, params string[] carried)
        where T : class
    {
        var planted = new Dictionary<string, string>();

        foreach (var property in typeof(T).GetProperties().Where(p => { return p.PropertyType == typeof(string) && !carried.Contains(p.Name); }))
        {
            planted[property.Name] = Planted + property.Name;
            property.SetValue(row, planted[property.Name]);
        }

        return planted;
    }

    /// <summary>A secret planted in every column the section does not name, of a database and of a user, never appears in it.</summary>
    [Fact]
    public async Task A_secret_planted_in_a_column_the_section_does_not_name_never_appears_in_it()
    {
        var account = Guid.NewGuid();
        await using var context = DatabasesTestContext.Create(FakeCurrentUser.Admin());
        var database = DatabasesTestContext.Row(account, "acct", "shop");
        var user = new DatabaseUser(Guid.NewGuid(), account, "reader", "acct_reader", database.CreatedAt);
        user.ReplacePrivileges(database.Id, DatabasePrivilegeSet.Select | DatabasePrivilegeSet.ShowView, Guid.NewGuid(), database.CreatedAt);
        var plantedDatabase = Plant(database, "Name", "DbUserNameSuffix");
        var plantedUser = Plant(user, "NameSuffix");
        context.Databases.Add(database);
        context.DatabaseUsers.Add(user);
        await context.SaveChangesAsync();

        var section = await new DatabasesMigrationExporter(context).ExportAsync(account, CancellationToken.None);

        Assert.NotEmpty(plantedDatabase);
        Assert.NotEmpty(plantedUser);
        Assert.NotNull(section);
        Assert.Contains("shop", section.Json, StringComparison.Ordinal);
        Assert.Contains("reader", section.Json, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, section.Json, StringComparison.Ordinal);
    }

    /// <summary>The additional user travels with its grants, by privilege name, and another account's user does not travel at all.</summary>
    [Fact]
    public async Task An_additional_user_travels_with_its_grants_and_a_strangers_does_not()
    {
        var account = Guid.NewGuid();
        await using var context = DatabasesTestContext.Create(FakeCurrentUser.Admin());
        var database = DatabasesTestContext.Row(account, "acct", "shop");
        var user = new DatabaseUser(Guid.NewGuid(), account, "reader", "acct_reader", database.CreatedAt);
        user.ReplacePrivileges(database.Id, DatabasePrivilegeSet.Select | DatabasePrivilegeSet.ShowView, Guid.NewGuid(), database.CreatedAt);
        context.Databases.Add(database);
        context.DatabaseUsers.Add(user);
        context.DatabaseUsers.Add(new DatabaseUser(Guid.NewGuid(), Guid.NewGuid(), "stranger", "other_stranger", database.CreatedAt));
        await context.SaveChangesAsync();

        var section = await new DatabasesMigrationExporter(context).ExportAsync(account, CancellationToken.None);

        using var document = JsonDocument.Parse(section!.Json);
        var users = document.RootElement.GetProperty("users");
        var only = Assert.Single(users.EnumerateArray());
        Assert.Equal("reader", only.GetProperty("nameSuffix").GetString());
        var grant = Assert.Single(only.GetProperty("grants").EnumerateArray());
        Assert.Equal("shop", grant.GetProperty("databaseName").GetString());
        Assert.Equal(["Select", "ShowView"], grant.GetProperty("privileges").EnumerateArray().Select(p => { return p.GetString(); }));
        Assert.DoesNotContain("stranger", section.Json, StringComparison.Ordinal);
    }

    /// <summary>The positive control: serialising the rows themselves DOES carry the planted values.</summary>
    [Fact]
    public void The_probe_can_see_a_planted_value_in_a_serialised_row()
    {
        var database = DatabasesTestContext.Row(Guid.NewGuid(), "acct", "shop");
        var planted = Plant(database, "Name", "DbUserNameSuffix");

        var json = JsonSerializer.Serialize(database, JsonSerializerOptions.Web);

        Assert.NotEmpty(planted);
        Assert.All(planted.Values, value => { Assert.Contains(value, json, StringComparison.Ordinal); });
    }
}
