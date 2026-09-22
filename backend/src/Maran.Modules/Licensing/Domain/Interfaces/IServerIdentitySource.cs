namespace Maran.Modules.Licensing.Domain.Interfaces;

/// <summary>
/// Reads the identity of the server this panel is running on, for licence binding.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the verifier does not simply call the agent.</b> Verification is the one method in this
/// module that must never throw for any input (§229), and an RPC is the most throwing thing it
/// could do. Behind this interface the failure is a value — <see langword="null"/> — which the
/// binding policy then treats as the refusal it is, rather than an exception the verifier would
/// have to catch and interpret.
/// </para>
/// <para>
/// <b>The machine-id and nothing else.</b> The agent also reports the primary interface, and this
/// panel deliberately does not bind to it: it was measured unstable, changing with a DHCP renewal,
/// a VPN, or a moved cable. Exposing it here would invite a later caller to compare it.
/// </para>
/// </remarks>
public interface IServerIdentitySource
{
    /// <summary>Reads this host's machine-id.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The machine-id, or <see langword="null"/> when this host has none or could not be asked.
    /// </returns>
    /// <remarks>
    /// The two null cases are deliberately not distinguished: both mean "this panel cannot say
    /// which machine it is on", and a bound licence is refused on either. A caller tempted to tell
    /// them apart in order to let one of them through should read
    /// <c>LicenceServerBindingPolicy</c>'s remarks first.
    /// </remarks>
    Task<string?> TryReadMachineIdAsync(CancellationToken cancellationToken);
}
