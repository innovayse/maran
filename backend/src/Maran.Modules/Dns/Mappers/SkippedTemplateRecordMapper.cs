using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.Modules.Dns.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Dns.Mappers;

/// <summary>Restates the template records a new zone did not get as the wire shape, with the reason in words.</summary>
public static class SkippedTemplateRecordMapper
{
    /// <summary>Maps one skipped record.</summary>
    /// <param name="skipped">The record that was left out.</param>
    /// <param name="names">The viewer's-language lookup of the module's display names.</param>
    /// <returns>The wire shape.</returns>
    public static SkippedTemplateRecordDto From(SkippedTemplateRecord skipped, IStringLocalizer<DisplayNames> names)
    {
        return new SkippedTemplateRecordDto(
            skipped.Name,
            skipped.Type,
            skipped.Type.ToString().ToUpperInvariant(),
            TemplateTokenRules.TokenOf(skipped.Variable),
            names[$"DnsTemplateSkipped{skipped.Variable}"].Value);
    }
}
