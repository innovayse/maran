using Maran.Modules.Firewall.Domain.Entities;
using Maran.Sdk.Validators;

namespace Maran.Modules.Firewall.Queries.ListBans;

/// <summary>
/// Holds <see cref="ListBansQuery"/> to the panel's paging bounds (rules/security.md "Input"). The list
/// has no typed filter of its own to validate.
/// </summary>
public sealed class ListBansQueryValidator : PagedQueryValidator<ListBansQuery, BanEpisode>
{
    /// <summary>Declares the paging rules against <see cref="ListBansSorts.Map"/>.</summary>
    public ListBansQueryValidator()
        : base(ListBansSorts.Map)
    {
    }
}
