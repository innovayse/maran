using Maran.Sdk.Contracts;

namespace Maran.Sdk.Tests.Contracts;

/// <summary>
/// Covers <see cref="ModuleEntitlement"/>, the value every module and filter can read: it carries a module id,
/// a verdict and a date and nothing that could hold secret material. A member added later fails this test and
/// forces the question "why does every module need to see it?" (docs/superpowers/notes/2026-11-07-licensing-lease-threat-note.md).
/// </summary>
public sealed class ModuleEntitlementTests
{
    /// <summary>The window's value has exactly the three members the contract names.</summary>
    [Fact]
    public void The_entitlement_carries_a_module_id_a_verdict_and_a_date_and_nothing_else()
    {
        var members = typeof(ModuleEntitlement)
            .GetProperties()
            .Select(property => { return property.Name + ":" + property.PropertyType.Name; })
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            ["ModuleId:String", "ValidUntil:Nullable`1", "Verdict:EntitlementVerdict"],
            members);
    }

    /// <summary>
    /// The lease value the sources hand over carries no key material either: its text members are the licence id
    /// and the machine id, and the three dates it needs. The control for the test above: the check does list
    /// members, so a "Signature" or "DecryptionKey" added to either record would show.
    /// </summary>
    [Fact]
    public void The_lease_carries_ids_and_dates_and_no_signature_or_key()
    {
        var members = typeof(LicenceLease)
            .GetProperties()
            .Select(property => { return property.Name; })
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["IssuedAt", "LicenceId", "MachineId", "ValidUntil"], members);
    }

    /// <summary>Two entitlements with the same fields are equal, so a cached answer can be compared.</summary>
    [Fact]
    public void Entitlements_with_the_same_fields_are_equal()
    {
        var at = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            new ModuleEntitlement("mail", EntitlementVerdict.Covered, at),
            new ModuleEntitlement("mail", EntitlementVerdict.Covered, at));
        Assert.NotEqual(
            new ModuleEntitlement("mail", EntitlementVerdict.Covered, at),
            new ModuleEntitlement("mail", EntitlementVerdict.Lapsed, at));
    }
}
