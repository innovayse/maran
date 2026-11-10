using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Dns.Mappers;

/// <summary>
/// Restates the closed catalogue of template variables for the editor, with the words in the viewer's language.
/// The catalogue and where each variable is allowed are read from <see cref="TemplateTokenRules"/>, so the editor
/// never holds a list of its own. Translates; never decides.
/// </summary>
public static class TemplateVariableMapper
{
    /// <summary>Maps every variable of the catalogue.</summary>
    /// <param name="names">The viewer's-language lookup of the module's display names.</param>
    /// <returns>The variables, in the order the catalogue lists them.</returns>
    public static IReadOnlyList<TemplateVariableDto> Catalogue(IStringLocalizer<DisplayNames> names)
    {
        return TemplateTokenRules.All.Select(variable =>
        {
            return new TemplateVariableDto(
                TemplateTokenRules.TokenOf(variable),
                names[$"DnsTemplateVariable{variable}"].Value,
                Enum.GetValues<RecordType>()
                    .Where(type =>
                    {
                        return TemplateTokenRules.AllowedIn(type).Contains(variable);
                    })
                    .ToList());
        }).ToList();
    }
}
