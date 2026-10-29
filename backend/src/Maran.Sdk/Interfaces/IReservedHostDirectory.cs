namespace Maran.Sdk.Interfaces;

/// <summary>
/// Answers whether a host name is the panel's own, so a module can refuse to let a tenant claim it.
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
    /// <summary>Whether a candidate is the panel's configured host name or its <c>www.</c> form.</summary>
    /// <param name="candidate">The host name as the caller submitted it.</param>
    /// <returns>
    /// True for the configured panel name and its <c>www.</c> form, compared case-insensitively;
    /// false for any other name, including names under the panel's domain, and ALWAYS false when
    /// no panel name is configured (the failing direction is permissive, the static rule being
    /// unconditional).
    /// </returns>
    bool IsPanelHostName(string candidate);
}
