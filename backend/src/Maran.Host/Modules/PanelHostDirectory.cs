using System.Diagnostics.CodeAnalysis;
using Maran.Host.Configuration;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.Extensions.Options;

namespace Maran.Host.Modules;

/// <summary>
/// The panel's <see cref="IReservedHostDirectory"/> and <see cref="IPanelHostName"/>: answers whether
/// a host name is one the panel is configured to be reached by, or the <c>www.</c> form of one, and
/// hands the vhost's name to the panel's own certificate issuance (issue #132).
/// </summary>
/// <remarks>
/// <para>
/// <b>The names are two, and this is the one place that knows both (issue #468).</b> The panel is
/// reached by the name its own vhost serves (<see cref="PanelHostOptions.HostName"/>, from
/// <c>hostname -f</c>) AND by the name the operator tells customers it lives at
/// (<see cref="PanelHostOptions.PublicUrl"/>), which the installer lets them set to anything. A tenant
/// who may hold a vhost for either one answers it on 80 and 443, ACME HTTP-01 included, and can obtain
/// a publicly trusted certificate for it, so a name is reserved when it is either of them. Every door
/// asks this one directory (site create and its aliases, certificate issue and install, zone create);
/// there is no second list. <see cref="IPanelHostName.TryGet"/> still hands out only the vhost's name,
/// because the panel's own certificate is ordered for the name its vhost serves.
/// </para>
/// <para>
/// <b>It lives in the Host because only the Host may read <see cref="PanelHostOptions"/>.</b> A
/// module cannot reference another module's options (rules/architecture.md), so the Sites, Ssl and
/// Ftp modules ask the Sdk window and get a verdict, never the name.
/// </para>
/// <para>
/// <b>The failing direction is permissive, on purpose.</b> A host name that is empty, is the
/// installer's placeholder <c>_</c>, is <c>localhost</c> (already in the static reserved set), or is
/// not a well-formed host name is treated as "not configured", and so is a public address that is not
/// an acceptable origin or whose host is an IP address (already in the static reserved set): with
/// neither, the answer is always false and the unconditional static rule remains the protection. A
/// malformed value is never matched loosely, because a loose match would refuse legitimate tenant names.
/// </para>
/// <para>
/// Comparison is ordinal and case-insensitive after dropping one trailing dot, the same
/// normalisation <see cref="ReservedHostNameRule"/> applies. Names under the panel's domain are
/// not the panel's own and are not matched. The options are read once, at construction: a changed
/// value takes effect on restart, like every other setting from <c>panel.env</c>.
/// </para>
/// </remarks>
public sealed class PanelHostDirectory : IReservedHostDirectory, IPanelHostName
{
    /// <summary>The prefix of the alternative spelling every public name is also reached by.</summary>
    private const string WwwPrefix = "www.";

    /// <summary>The installer's placeholder for "no usable name", which is not a name.</summary>
    private const string Placeholder = "_";

    /// <summary>The normalised name the panel's own vhost serves, or <see langword="null"/> when none is configured.</summary>
    private readonly string? _panelName;

    /// <summary>Every normalised name the panel is reached by: the vhost's and the public address's, each once.</summary>
    private readonly string[] _reservedNames;

    /// <summary>Creates the directory from the configured panel names.</summary>
    /// <param name="options">The Host's panel-name options.</param>
    public PanelHostDirectory(IOptions<PanelHostOptions> options)
    {
        _panelName = Normalise(options.Value.HostName);

        var publicName = PublicUrlRule.TryGetDnsName(options.Value.PublicUrl, out var host)
            ? Normalise(host)
            : null;
        _reservedNames = new[] { _panelName, publicName }
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <inheritdoc />
    public bool IsPanelHostName(string candidate)
    {
        if (_reservedNames.Length == 0 || string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        var name = DropTrailingDot(candidate);
        foreach (var reserved in _reservedNames)
        {
            if (name.Equals(reserved, StringComparison.OrdinalIgnoreCase)
                || name.Equals(WwwPrefix + reserved, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool TryGet([NotNullWhen(true)] out string? hostName)
    {
        hostName = _panelName;
        return hostName is not null;
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
