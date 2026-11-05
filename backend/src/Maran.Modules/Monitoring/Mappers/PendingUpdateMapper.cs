using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Domain.Policies;
using Maran.Modules.Monitoring.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Monitoring.Mappers;

/// <summary>Translates what the agent reported for one pending update into the shape the API sends.</summary>
/// <remarks>
/// Restates and never decides: the client boundary has already turned an absent installed version
/// into <c>null</c> and an absent source into an empty string, once, so every field crosses as it arrived;
/// the classification is worded by <see cref="PendingUpdateClassificationPolicy"/>, the one place the
/// three words are spelled, and their names come from the module's display names.
/// </remarks>
public static class PendingUpdateMapper
{
    /// <summary>Builds the wire row for one pending update.</summary>
    /// <param name="update">What the agent reported.</param>
    /// <param name="displayNames">The module's display names, resolved in the request's culture.</param>
    /// <returns>The row.</returns>
    public static PendingUpdateDto ToDto(AgentPendingUpdate update, IStringLocalizer<DisplayNames> displayNames)
    {
        var classification = PendingUpdateClassificationPolicy.NameOf(update.Classification);

        return new PendingUpdateDto(
            update.PackageName,
            update.AvailableVersion,
            update.InstalledVersion,
            update.Source,
            classification,
            NameOf(classification, displayNames));
    }

    /// <summary>Builds the choice that offers one classification word.</summary>
    /// <param name="classification">One of <see cref="PendingUpdateClassificationPolicy.All"/>.</param>
    /// <param name="displayNames">The module's display names, resolved in the request's culture.</param>
    /// <returns>The word and its name.</returns>
    public static ClassificationChoiceDto ToChoice(string classification, IStringLocalizer<DisplayNames> displayNames)
    {
        return new ClassificationChoiceDto(classification, NameOf(classification, displayNames));
    }

    /// <summary>Names one classification word in the request's culture.</summary>
    /// <param name="classification">One of <see cref="PendingUpdateClassificationPolicy.All"/>.</param>
    /// <param name="displayNames">The module's display names.</param>
    /// <returns>The name an administrator reads.</returns>
    private static string NameOf(string classification, IStringLocalizer<DisplayNames> displayNames)
    {
        return displayNames[PendingUpdateClassificationPolicy.DisplayNameKeyOf(classification)].Value;
    }
}
