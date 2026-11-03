using Maran.Modules.Firewall.Common;
using Maran.Modules.Firewall.Domain.Policies;
using Maran.Modules.Firewall.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Firewall.Queries.ListPortPresets;

/// <summary>
/// Handles <see cref="ListPortPresetsQuery"/> by naming every preset of
/// <see cref="FirewallPresetCatalog"/> for the request's culture.
/// </summary>
/// <remarks>
/// A name is looked up as <c>FirewallPreset</c> plus the member name; <see cref="IStringLocalizer"/>
/// answers a missing key with the key, so the miss is detected and the member name is shown instead,
/// which keeps a preset added to the catalog usable before anyone has translated it.
/// </remarks>
public sealed class ListPortPresetsQueryHandler
{
    /// <summary>The prefix a preset's resx key carries, so presets cannot collide with other names.</summary>
    private const string KeyPrefix = "FirewallPreset";

    /// <summary>Resolves this module's display-name resources for the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="displayNames">This module's display-name resources.</param>
    public ListPortPresetsQueryHandler(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Returns every preset, named for the caller.</summary>
    /// <param name="query">The (parameterless) request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the presets; this operation never fails.</returns>
    public Task<Result<IReadOnlyList<FirewallPresetDto>>> HandleAsync(
        ListPortPresetsQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FirewallPresetDto> presets = FirewallPresetCatalog.All
            .Select(preset =>
            {
                var memberName = preset.Key.ToString();
                var localized = _displayNames[KeyPrefix + memberName];

                var name = localized.ResourceNotFound ? memberName : localized.Value;

                var rules = preset.Rules
                    .Select(rule =>
                    {
                        return new FirewallRuleDto(rule.Port, rule.PortTo, rule.Protocol, rule.SourceCidr);
                    })
                    .ToList();

                return new FirewallPresetDto(preset.Key, name, preset.Closable, rules);
            })
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<FirewallPresetDto>>.Ok(presets));
    }
}
