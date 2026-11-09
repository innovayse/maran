using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.SitesService;

/// <summary>A site's IP rules: the networks, and whether they are the only ones allowed or the ones refused.</summary>
/// <param name="Mode">Whether <paramref name="Cidrs"/> is an allow-list or a deny-list.</param>
/// <param name="Cidrs">The addresses or CIDR ranges; never empty (a site with none carries no <see cref="IpAccessDescriptor"/>).</param>
public sealed record IpAccessDescriptor(IpAccessKind Mode, IReadOnlyList<string> Cidrs)
{
    /// <summary>Converts these rules into their wire message.</summary>
    /// <returns>The populated <see cref="IpAccess"/>.</returns>
    internal IpAccess ToWire()
    {
        var wire = new IpAccess
        {
            Mode = Mode switch
            {
                IpAccessKind.AllowList => IpAccessMode.AllowList,
                IpAccessKind.DenyList => IpAccessMode.DenyList,
                _ => throw new ArgumentOutOfRangeException(nameof(Mode), Mode, "Unknown IP access kind."),
            },
        };
        wire.Cidrs.AddRange(Cidrs);

        return wire;
    }
}
