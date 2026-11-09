namespace Maran.Sdk.Contracts;

/// <summary>A site's IP rules, as one module reports them to another.</summary>
/// <param name="Mode">Whether <paramref name="Cidrs"/> is an allow-list or a deny-list.</param>
/// <param name="Cidrs">The addresses or CIDR ranges; never empty (a site with none reports no <see cref="SiteIpAccessSnapshot"/>).</param>
public sealed record SiteIpAccessSnapshot(SiteIpAccessMode Mode, IReadOnlyList<string> Cidrs);
