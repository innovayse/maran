using System.Text.Json;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;

namespace Maran.Modules.Accounts.Tests.Services;

/// <summary>
/// What the Accounts section of an account move holds, and that nothing else of the account row or the plan row
/// ever reaches it (issue #127; rules/security.md item 8).
/// </summary>
public sealed class AccountsMigrationExporterTests
{
    /// <summary>The prefix of every value planted in a column the section does not name.</summary>
    private const string Planted = "PLANTED-SECRET-";

    /// <summary>The fields the section carries, and no others.</summary>
    private static readonly string[] AllowedFields = ["name", "primaryDomain", "ownerEmail", "ownerLanguage", "planName", "shellAccess"];

    /// <summary>Fragments no exported field name may contain: the vocabulary of secrets.</summary>
    private static readonly string[] ForbiddenFragments = ["password", "hash", "secret", "token", "privatekey", "session", "totp"];

    /// <summary>Plants a recognisable value in every string column of the plan row except the one the section carries (its name).</summary>
    /// <param name="plan">The plan row.</param>
    /// <returns>The planted values, by property name.</returns>
    private static Dictionary<string, string> PlantInPlan(Plan plan)
    {
        var planted = new Dictionary<string, string>();

        foreach (var property in typeof(Plan).GetProperties().Where(p => { return p.PropertyType == typeof(string) && p.Name != "Name"; }))
        {
            planted[property.Name] = Planted + property.Name;
            property.SetValue(plan, planted[property.Name]);
        }

        return planted;
    }

    /// <summary>The section holds exactly the allow-listed fields, none named like a secret, and no planted value from the plan row.</summary>
    [Fact]
    public async Task The_section_holds_exactly_the_allow_listed_fields_and_none_of_a_planted_secret()
    {
        await using var context = AccountsTestContext.Create();
        var plan = new Plan(Guid.NewGuid(), "StarterPlan", 1024, 5, 5, 5, 5, 2);
        var planted = PlantInPlan(plan);
        var account = new Account(Guid.NewGuid(), "alice", "alice.example", plan.Id, DateTimeOffset.UnixEpoch, "alice@example.com");
        context.Plans.Add(plan);
        context.Accounts.Add(account);
        await context.SaveChangesAsync();

        var section = await new AccountsMigrationExporter(context).ExportAsync(account.Id, CancellationToken.None);

        Assert.NotNull(section);
        using var document = JsonDocument.Parse(section.Json);
        var names = document.RootElement.EnumerateObject().Select(property => { return property.Name; }).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(AllowedFields.Order(StringComparer.Ordinal), names);
        Assert.All(names, name =>
        {
            Assert.DoesNotContain(ForbiddenFragments, fragment => { return name.Contains(fragment, StringComparison.OrdinalIgnoreCase); });
        });
        Assert.NotEmpty(planted);
        Assert.DoesNotContain(Planted, section.Json, StringComparison.Ordinal);
    }

    /// <summary>The positive control: serialising the plan row itself DOES carry the planted values, so the probe can see a leak.</summary>
    [Fact]
    public void The_probe_can_see_a_planted_value_in_a_serialised_row()
    {
        var plan = new Plan(Guid.NewGuid(), "StarterPlan", 1024, 5, 5, 5, 5, 2);
        var planted = PlantInPlan(plan);

        var json = JsonSerializer.Serialize(plan, JsonSerializerOptions.Web);

        Assert.NotEmpty(planted);
        Assert.All(planted.Values, value => { Assert.Contains(value, json, StringComparison.Ordinal); });
    }

    /// <summary>An account that does not exist has no section.</summary>
    [Fact]
    public async Task An_unknown_account_has_no_section()
    {
        await using var context = AccountsTestContext.Create();

        Assert.Null(await new AccountsMigrationExporter(context).ExportAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
