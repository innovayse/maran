namespace Maran.Sdk.Interfaces;

/// <summary>
/// Answers whether a host name is one the panel itself is served under, so a module can refuse to let a
/// tenant claim it. The panel has more than one such name: the one its own vhost serves and the host of
/// the public address the operator advertises (issue #468). This is the single source for both, asked by
/// site create and its aliases, certificate issue and install, and zone create.
/// The contract lives in the Sdk and the implementation in the Host, which alone reads the
/// configured panel name; a module may never reference another module's options type
/// (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// <para>
/// The host-specific half of the reserved set. The static half (address literals, loopback and
/// special-use names) is <c>Maran.SharedKernel.Utilities.Network.ReservedHostNameRule</c>, which
/// needs no configuration and so is not behind this interface. See
/// docs/superpowers/notes/2026-10-26-reserved-hostnames-threat-note.md.
/// </para>
/// <para>
/// Deliberately the narrowest value that answers the one question: it returns a verdict and never
/// the panel's name, so a caller cannot echo the name back to a tenant.
/// </para>
/// </remarks>
public interface IReservedHostDirectory
{
    /// <summary>Whether a candidate is one of the panel's configured host names or the <c>www.</c> form of one.</summary>
    /// <param name="candidate">The host name as the caller submitted it.</param>
    /// <returns>
    /// True for each configured panel name (the vhost's and the public address's host) and its
    /// <c>www.</c> form, compared case-insensitively; false for any other name, including names
    /// under the panel's domain, and ALWAYS false when no panel name is configured (the failing direction is permissive, the static rule being
    /// unconditional).
    /// </returns>
    bool IsPanelHostName(string candidate);
}
