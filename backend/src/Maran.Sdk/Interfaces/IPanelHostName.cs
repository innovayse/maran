using System.Diagnostics.CodeAnalysis;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// Gives the panel's own configured host name to the one module that needs the name itself: Ssl,
/// which orders the panel's certificate for it (issue #132). Implemented by the Host, which alone
/// reads the configured value (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// A window separate from <see cref="IReservedHostDirectory"/> on purpose. That one answers a
/// tenant-facing question with a verdict and never the name, so a caller cannot echo the name back
/// to a tenant; this one hands out the name, and is consumed only by an administrator-only path.
/// Keeping them apart keeps the narrow window narrow.
/// </remarks>
public interface IPanelHostName
{
    /// <summary>Reads the panel's configured host name.</summary>
    /// <param name="hostName">The configured name, trimmed and without a trailing dot, when there is one.</param>
    /// <returns>
    /// False when no usable name is configured — empty, the installer's placeholder, <c>localhost</c>,
    /// or not a well-formed host name — which is the same reading <see cref="IReservedHostDirectory"/>
    /// gives an unusable value.
    /// </returns>
    bool TryGet([NotNullWhen(true)] out string? hostName);
}
