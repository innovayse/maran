using Maran.Host.Configuration;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.Extensions.Options;

namespace Maran.Host.Modules;

/// <summary>
/// The panel's <see cref="IReservedHostDirectory"/>: answers whether a host name is the one the
/// panel is configured to be reached by, or its <c>www.</c> form.
/// </summary>
/// <remarks>
/// <para>
/// <b>It lives in the Host because only the Host may read <see cref="PanelHostOptions"/>.</b> A
/// module cannot reference another module's options (rules/architecture.md), so the Sites, Ssl and
/// Ftp modules ask the Sdk window and get a verdict, never the name.
/// </para>
/// <para>
/// <b>The failing direction is permissive, on purpose.</b> A value that is empty, is the
/// installer's placeholder <c>_</c>, is <c>localhost</c> (already in the static reserved set), or is
/// not a well-formed host name is treated as "no panel name configured": the answer is then always
/// false, and the unconditional static rule remains the protection. A malformed value is never
/// matched loosely, because a loose match would refuse legitimate tenant names.
/// </para>
/// <para>
/// Comparison is ordinal and case-insensitive after dropping one trailing dot, the same
/// normalisation <see cref="ReservedHostNameRule"/> applies. Names under the panel's domain are
/// not the panel's own and are not matched. The options are read once, at construction: a changed
/// value takes effect on restart, like every other setting from <c>panel.env</c>.
/// </para>
/// </remarks>
public sealed class PanelHostDirectory : IReservedHostDirectory
{
    /// <summary>The prefix of the alternative spelling every public name is also reached by.</summary>
    private const string WwwPrefix = "www.";

    /// <summary>The installer's placeholder for "no usable name", which is not a name.</summary>
    private const string Placeholder = "_";

    /// <summary>The normalised panel name, or <see langword="null"/> when none is configured.</summary>
    private readonly string? _panelName;

    /// <summary>Creates the directory from the configured panel name.</summary>
    /// <param name="options">The Host's panel-name options.</param>
    public PanelHostDirectory(IOptions<PanelHostOptions> options)
    {
        _panelName = Normalise(options.Value.HostName);
    }

    /// <inheritdoc />
    public bool IsPanelHostName(string candidate)
    {
        if (_panelName is null || string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        var name = DropTrailingDot(candidate);
        return name.Equals(_panelName, StringComparison.OrdinalIgnoreCase)
            || name.Equals(WwwPrefix + _panelName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reduces a configured value to the one spelling it is compared in.</summary>
    /// <param name="configured">The raw configured value.</param>
    /// <returns>
    /// The trimmed, trailing-dot-free value, or <see langword="null"/> when it is empty, the
    /// placeholder, <c>localhost</c> or not a well-formed host name.
    /// </returns>
    private static string? Normalise(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        var name = DropTrailingDot(configured.Trim());
        if (name == Placeholder
            || name.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || !HostNameRule.IsHostName(name))
        {
            return null;
        }

        return name;
    }

    /// <summary>Drops one trailing dot, the fully-qualified spelling of the same name.</summary>
    /// <param name="name">The name to reduce.</param>
    /// <returns>The name without one trailing dot.</returns>
    private static string DropTrailingDot(string name)
    {
        return name.EndsWith('.') ? name[..^1] : name;
    }
}
