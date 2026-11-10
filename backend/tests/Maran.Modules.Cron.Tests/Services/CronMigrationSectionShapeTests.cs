using Maran.Modules.Cron.Models;

namespace Maran.Modules.Cron.Tests.Services;

/// <summary>
/// The Cron section of an account move is an allow-list of named fields: the records the exporter fills have
/// exactly the members below and none named like a secret (issue #127; rules/security.md item 8).
/// </summary>
/// <remarks>
/// The exporter reads through the module's bus, so a row with a secret planted in it cannot be arranged without a
/// whole bus; this holds the SHAPE the exporter builds instead. A cron command and an environment value are the
/// customer's own data and travel on purpose (decision D8): the record has no member that could carry anything else.
/// </remarks>
public sealed class CronMigrationSectionShapeTests
{
    /// <summary>Fragments no member name may contain: the vocabulary of secrets the panel itself holds.</summary>
    private static readonly string[] ForbiddenFragments = ["password", "hash", "secret", "token", "privatekey", "output", "stdout", "stderr", "log"];

    /// <summary>The members of a type, by name.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The public instance property names, ordered.</returns>
    private static List<string> MembersOf(Type type)
    {
        return type.GetProperties().Select(property => { return property.Name; }).Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>The section and its entry hold exactly the allow-listed members.</summary>
    [Fact]
    public void The_section_and_its_entry_hold_exactly_the_allow_listed_members()
    {
        Assert.Equal(["Entries", "Environment"], MembersOf(typeof(CronMigrationSection)));
        Assert.Equal(["Command", "Enabled", "Schedule"], MembersOf(typeof(CronEntryMigrationItem)));
    }

    /// <summary>No member is named like a secret, and the probe can tell: it flags a type that has one.</summary>
    [Fact]
    public void No_member_is_named_like_a_secret_and_the_probe_can_see_one_that_is()
    {
        var names = MembersOf(typeof(CronMigrationSection)).Concat(MembersOf(typeof(CronEntryMigrationItem))).ToList();

        Assert.All(names, name => { Assert.DoesNotContain(ForbiddenFragments, fragment => { return name.Contains(fragment, StringComparison.OrdinalIgnoreCase); }); });
        Assert.Contains(MembersOf(typeof(SecretBearing)), name => { return ForbiddenFragments.Any(fragment => { return name.Contains(fragment, StringComparison.OrdinalIgnoreCase); }); });
    }

    /// <summary>A type with a secret-named member, the positive control for the probe above.</summary>
    /// <param name="Password">The member the probe must flag.</param>
    private sealed record SecretBearing(string Password);
}
