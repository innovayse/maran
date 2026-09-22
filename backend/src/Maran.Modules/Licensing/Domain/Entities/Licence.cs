using Maran.Modules.Licensing.Domain.ValueObjects;

namespace Maran.Modules.Licensing.Domain.Entities;

/// <summary>
/// A licence artefact whose signature, product and expiry have already been checked, as parsed
/// from a §228 envelope: id, product, tier, the modules it licenses, and its expiry.
/// </summary>
/// <remarks>
/// <para>
/// This pass (offline signature-and-expiry verification only, per this slice's narrowed scope) does
/// not persist this type — no EF mapping, no <c>Persistence/</c> project reference. Wiring it into
/// the panel's own PostgreSQL schema (so the "currently installed" licence is a fact read from the
/// database rather than re-verified from a file on every call) is explicitly deferred; see this
/// module's log entry in, so there is nothing on this type
/// yet for such a field to be checked against.
/// </para>
/// </remarks>
public sealed class Licence
{
    /// <summary>The licence's own opaque identifier.</summary>
    public LicenceId Id { get; }

    /// <summary>The product this licence was issued for; compared against this build's own product id.</summary>
    public string Product { get; }

    /// <summary>The plan tier this licence carries.</summary>
    public string Tier { get; }

    /// <summary>The module ids this licence unlocks.</summary>
    public IReadOnlyList<string> Modules { get; }

    /// <summary>The instant after which this licence is no longer valid.</summary>
    public DateTimeOffset Expiry { get; }

    /// <summary>Creates a parsed, not-yet-verified licence value.</summary>
    /// <param name="id">The licence's own opaque identifier.</param>
    /// <param name="product">The product this licence was issued for.</param>
    /// <param name="tier">The plan tier this licence carries.</param>
    /// <param name="modules">The module ids this licence unlocks.</param>
    /// <param name="expiry">The instant after which this licence is no longer valid.</param>
    public Licence(LicenceId id, string product, string tier, IReadOnlyList<string> modules, DateTimeOffset expiry)
    {
        Id = id;
        Product = product;
        Tier = tier;
        Modules = modules;
        Expiry = expiry;
    }
}
