namespace Maran.Modules.Ftp.Domain.Policies;

/// <summary>
/// The numbers this panel chooses for an FTPS daemon it installs. The ONLY place they exist — not
/// in the SPA, which holds no domain constants (rules/architecture.md "The backend owns the data,
/// the SPA renders it"), and not in the agent, which receives them on the wire and owns no product
/// default of its own.
/// </summary>
/// <remarks>
/// <para>
/// A stateless rule over values, so <c>Domain/Policies/</c> rather than <c>Common/</c>, which holds
/// <c>*Dto.cs</c> and nothing else (rules/csharp.md).
/// </para>
/// <para>
/// <b>Why a policy and not a settings class an operator edits.</b> The range has to be the same
/// range the firewall opens, and the ceiling has to be a number the range can serve. Two operators
/// editing two settings independently is how a daemon ends up advertising ports nothing lets
/// through, so the pair is decided here, together, and moved together.
/// </para>
/// </remarks>
public static class FtpsDefaults
{
    /// <summary>The control port an FTPS client connects to first.</summary>
    /// <remarks>
    /// Port 21, explicit rather than implied. It is here and not in the agent because the panel is
    /// what tells an operator which port to open, and a number an operator is told must be the same
    /// number the daemon was configured with.
    /// </remarks>
    public const int ControlPort = 21;

    /// <summary>Lowest port of the passive data range, inclusive.</summary>
    /// <remarks>
    /// 30000 is high enough to sit well clear of anything a distribution assigns and low enough to
    /// stay inside every ephemeral-port arrangement we support. Conservative on purpose: the range
    /// can be moved later without changing the shape of anything, because it travels on the wire.
    /// </remarks>
    public const int PassivePortMin = 30000;

    /// <summary>Highest port of the passive data range, inclusive.</summary>
    /// <remarks>
    /// A hundred ports for a hundred clients (<see cref="MaxClients"/>), so the range can never be
    /// what runs out first. A range narrower than the client ceiling fails in the worst possible
    /// way — the control connection succeeds, the customer authenticates, and the transfer then
    /// stalls with no error anyone can act on.
    /// </remarks>
    public const int PassivePortMax = 30099;

    /// <summary>The daemon's concurrent-session ceiling.</summary>
    /// <remarks>
    /// A hundred, matched one-to-one with the passive range above. Conservative, and movable later
    /// — but only together with the range.
    /// </remarks>
    public const int MaxClients = 100;
}
