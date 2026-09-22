using Maran.Modules.Licensing.Domain.Entities;

namespace Maran.Modules.Licensing.Domain.Policies;

/// <summary>
/// Decides whether a licence bound to one server is being presented on a different one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is its own type, like <see cref="LicenceExpiryPolicy"/>.</b> The whole commercial
/// value of binding is one comparison, and a comparison buried inside a long verification method is
/// a comparison nobody can aim a mutation at. Isolated here, "the binding is ignored" has exactly
/// one place to land, and a test that claims to guard it either kills the mutant or admits it does
/// not.
/// </para>
/// <para>
/// <b>The three inputs and what each means, stated rather than left to the reader.</b> A licence
/// with no <see cref="Licence.BoundMachineId"/> names no server: it runs anywhere, and this policy
/// says so. A licence that names one is refused on every host but that one. And a host whose own
/// machine-id could NOT be read is refused as well — see the next paragraph, because that case is
/// the one where a mistake pays an attacker.
/// </para>
/// <para>
/// <b>Unreadable means refused, deliberately.</b> The tempting reading of "we could not determine
/// this host's identity" is to let the licence through, because the failure is the panel's rather
/// than the customer's. That reading hands anyone who can stop the agent, or empty one file, a
/// licence valid on every machine they own — the exact attack binding exists to stop. So an
/// unreadable identity refuses, and the cost is stated honestly: a genuine customer whose agent is
/// down sees their licence refused until it is back. That cost is bearable HERE and nowhere else,
/// because nothing in this panel yet gates a feature on licence status — refusal is reported, not
/// enforced. Should that change, this paragraph is the one to revisit first.
/// </para>
/// </remarks>
public static class LicenceServerBindingPolicy
{
    /// <summary>Decides whether the licence may run on the host with the given machine-id.</summary>
    /// <param name="licence">The licence whose binding is in question.</param>
    /// <param name="hostMachineId">
    /// This host's machine-id, or <see langword="null"/> when it could not be read.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the licence names a server that is not this one, or names one
    /// while this host's identity is unknown; <see langword="false"/> when it names no server, or
    /// names this one.
    /// </returns>
    public static bool IsBoundToAnotherServer(Licence licence, string? hostMachineId)
    {
        if (licence.BoundMachineId is null)
        {
            return false;
        }

        // Ordinal, case-insensitive: a machine-id is 32 hex digits, and the only way its case can
        // differ between the issuer's record and this host's file is transcription. Refusing over
        // that would be refusing over nothing.
        return !string.Equals(licence.BoundMachineId, hostMachineId, StringComparison.OrdinalIgnoreCase);
    }
}
