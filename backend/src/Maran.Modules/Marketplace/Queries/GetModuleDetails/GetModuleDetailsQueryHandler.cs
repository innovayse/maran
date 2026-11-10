using Maran.Modules.Marketplace.Common;
using Maran.Modules.Marketplace.Domain.Policies;
using Maran.Modules.Marketplace.Interfaces;
using Maran.Modules.Marketplace.Services;

namespace Maran.Modules.Marketplace.Queries.GetModuleDetails;

/// <summary>
/// Handles <see cref="GetModuleDetailsQuery"/>: one module's verified details, each version with whether it
/// can be installed now, and the server's copy of the signed manifest summary.
/// </summary>
/// <remarks>
/// Unlike the catalogue list, details that could not be verified are the request failing: there is nothing
/// to show for one module without them. The verification failure is journalled all the same.
/// </remarks>
public sealed class GetModuleDetailsQueryHandler
{
    /// <summary>The marketplace client.</summary>
    private readonly IMarketplaceClient _client;

    /// <summary>The eligibility rules, which read the panel-side facts.</summary>
    private readonly InstallEligibility _eligibility;

    /// <summary>Turns catalogue answers into wire shapes.</summary>
    private readonly MarketplaceCatalogueProjector _projector;

    /// <summary>The audit journal.</summary>
    private readonly MarketplaceAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="client">The marketplace client.</param>
    /// <param name="eligibility">The eligibility rules, which read the panel-side facts.</param>
    /// <param name="projector">Turns catalogue answers into wire shapes.</param>
    /// <param name="journal">The audit journal.</param>
    public GetModuleDetailsQueryHandler(
        IMarketplaceClient client,
        InstallEligibility eligibility,
        MarketplaceCatalogueProjector projector,
        MarketplaceAuditJournal journal)
    {
        _client = client;
        _eligibility = eligibility;
        _projector = projector;
        _journal = journal;
    }

    /// <summary>Reads one module's details.</summary>
    /// <param name="query">The validated query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The details, or the coded failure (<c>ModuleNotInCatalogue</c> for an unknown id).</returns>
    public async Task<Result<MarketplaceModuleDetailDto>> HandleAsync(
        GetModuleDetailsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var details = await _client.GetModuleAsync(query.Id, MarketplaceDisplayNames.Locale, cancellationToken);
        if (!details.IsSuccess)
        {
            if (CatalogueVerificationPolicy.IsVerificationFailure(details.Error!))
            {
                await _journal.RecordVerificationFailedAsync(
                    details.Error!, query.IpAddress, query.UserAgent, cancellationToken);
            }

            return Result<MarketplaceModuleDetailDto>.Fail(details.Error!);
        }

        var context = await _eligibility.LoadContextAsync(cancellationToken);
        var module = await _projector.ProjectModuleAsync(context, details.Value.Module, cancellationToken);
        var versions = await _projector.ProjectVersionsAsync(
            context, details.Value.Module, details.Value.Versions, cancellationToken);

        return Result<MarketplaceModuleDetailDto>.Ok(
            new MarketplaceModuleDetailDto(module, versions, _projector.ProjectManifest(details.Value.Manifest)));
    }
}
