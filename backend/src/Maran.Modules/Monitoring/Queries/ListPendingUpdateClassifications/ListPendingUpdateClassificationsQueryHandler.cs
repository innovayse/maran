using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Domain.Policies;
using Maran.Modules.Monitoring.Mappers;
using Maran.Modules.Monitoring.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Monitoring.Queries.ListPendingUpdateClassifications;

/// <summary>
/// Handles <see cref="ListPendingUpdateClassificationsQuery"/> by naming every word of
/// <see cref="PendingUpdateClassificationPolicy.All"/> for the request's culture.
/// </summary>
/// <remarks>
/// The answer is the policy's own list, in its order (security first), so it cannot disagree with what
/// the <c>classification</c> filter accepts or what a row carries: all three read the same words.
/// </remarks>
public sealed class ListPendingUpdateClassificationsQueryHandler
{
    /// <summary>Resolves this module's display names for the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="displayNames">This module's display names.</param>
    public ListPendingUpdateClassificationsQueryHandler(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Returns every classification an update can carry, named for the caller.</summary>
    /// <param name="query">The (parameterless) request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the choices; this operation never fails.</returns>
    public Task<Result<IReadOnlyList<ClassificationChoiceDto>>> HandleAsync(
        ListPendingUpdateClassificationsQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ClassificationChoiceDto> choices = PendingUpdateClassificationPolicy.All
            .Select(word =>
            {
                return PendingUpdateMapper.ToChoice(word, _displayNames);
            })
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<ClassificationChoiceDto>>.Ok(choices));
    }
}
