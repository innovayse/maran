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

    /// <summary>
    /// The machine-id of the one server this licence was issued for, or <see langword="null"/> when
    /// the licence names no server and may therefore run anywhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why nullable rather than always present.</b> Trial and development licences are issued
    /// before anyone knows which host will run them, and an issuer that had to name a server would
    /// have nothing to write. Absent is therefore a real state with its own meaning — "not bound" —
    /// and not a missing value: it is the difference between a licence that may run anywhere and one
    /// that may run on exactly one machine, which is the whole point of the field.
    /// </para>
    /// <para>
    /// <b>Only the machine-id, never the network interface.</b> The agent reads both, and the panel
    /// binds on this one alone. The primary interface was MEASURED unstable — on the development
    /// host it is WiFi, and it changes with a DHCP renewal, a VPN, a swapped cable or a different
    /// boot ordering — so binding to it would refuse honest customers on a Tuesday for no reason
    /// they could see. It is recorded as evidence and never compared.
    /// </para>
    /// </remarks>
    public string? BoundMachineId { get; }

    /// <summary>Creates a parsed, not-yet-verified licence value.</summary>
    /// <param name="id">The licence's own opaque identifier.</param>
    /// <param name="product">The product this licence was issued for.</param>
    /// <param name="tier">The plan tier this licence carries.</param>
    /// <param name="modules">The module ids this licence unlocks.</param>
    /// <param name="expiry">The instant after which this licence is no longer valid.</param>
    /// <param name="boundMachineId">
    /// The machine-id of the server this licence is issued for, or <see langword="null"/> when it
    /// names none and may run anywhere.
    /// </param>
    public Licence(
        LicenceId id,
        string product,
        string tier,
        IReadOnlyList<string> modules,
        DateTimeOffset expiry,
        string? boundMachineId = null)
    {
        Id = id;
        Product = product;
        Tier = tier;
        Modules = modules;
        Expiry = expiry;
        BoundMachineId = boundMachineId;
    }
}
