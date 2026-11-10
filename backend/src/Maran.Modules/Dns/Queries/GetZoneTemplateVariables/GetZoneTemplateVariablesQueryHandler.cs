using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Dns.Queries.GetZoneTemplateVariables;

/// <summary>
/// Handles <see cref="GetZoneTemplateVariablesQuery"/>. The editor keeps no list of variables of its own
/// (rules/architecture.md "The backend owns the data, the SPA renders it"): the catalogue and where each
/// variable is allowed are read from the very rules that refuse a template, and the words arrive translated.
/// </summary>
public sealed class GetZoneTemplateVariablesQueryHandler
{
    /// <summary>The viewer's-language lookup of <c>Resources/DisplayNames</c>.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="names">The viewer's-language lookup of the module's display names.</param>
    public GetZoneTemplateVariablesQueryHandler(IStringLocalizer<DisplayNames> names)
    {
        _names = names;
    }

    /// <summary>Builds the catalogue.</summary>
    /// <param name="query">The request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The catalogue; this operation never fails.</returns>
    public Task<Result<IReadOnlyList<TemplateVariableDto>>> HandleAsync(
        GetZoneTemplateVariablesQuery query,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<IReadOnlyList<TemplateVariableDto>>.Ok(TemplateVariableMapper.Catalogue(_names)));
    }
}
