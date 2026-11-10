using Maran.Modules.Marketplace.Common;
using Maran.Modules.Marketplace.Domain.Policies;
using Maran.Modules.Marketplace.Interfaces;
using Maran.Modules.Marketplace.Options;
using Maran.Modules.Marketplace.Services;

namespace Maran.Modules.Marketplace.Queries.ListCatalogue;

/// <summary>
/// Handles <see cref="ListCatalogueQuery"/>: one page of the verified catalogue, each module with what it
/// may do on this panel.
/// </summary>
/// <remarks>
/// <para>
/// <b>An unverified catalogue is an answer, not an error.</b> When the marketplace's answer arrived and
/// failed its checks (unsigned, stale, unreadable, no key to check it with) the page still needs its
/// installed list, so this is a 200 with <c>verified: false</c>, no items and a sentence saying why, and the
/// failure is journalled. A switched-off marketplace, an unreachable one or an unusable address is the
/// request failing, and answers with that code.
/// </para>
/// </remarks>
public sealed class ListCatalogueQueryHandler
{
    /// <summary>The marketplace client.</summary>
    private readonly IMarketplaceClient _client;

    /// <summary>The validated options, for the default channel.</summary>
    private readonly MarketplaceOptionsReader _options;

    /// <summary>The eligibility rules, which read the panel-side facts.</summary>
    private readonly InstallEligibility _eligibility;

    /// <summary>Turns catalogue answers into wire shapes.</summary>
    private readonly MarketplaceCatalogueProjector _projector;

    /// <summary>The words, in the caller's language.</summary>
    private readonly MarketplaceDisplayNames _names;

    /// <summary>The audit journal.</summary>
    private readonly MarketplaceAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="client">The marketplace client.</param>
    /// <param name="options">The validated options, for the default channel.</param>
    /// <param name="eligibility">The eligibility rules, which read the panel-side facts.</param>
    /// <param name="projector">Turns catalogue answers into wire shapes.</param>
    /// <param name="names">The words, in the caller's language.</param>
    /// <param name="journal">The audit journal.</param>
    public ListCatalogueQueryHandler(
        IMarketplaceClient client,
        MarketplaceOptionsReader options,
        InstallEligibility eligibility,
        MarketplaceCatalogueProjector projector,
        MarketplaceDisplayNames names,
        MarketplaceAuditJournal journal)
    {
        _client = client;
        _options = options;
        _eligibility = eligibility;
        _projector = projector;
        _names = names;
        _journal = journal;
    }

    /// <summary>Reads one page of the catalogue.</summary>
    /// <param name="query">The validated query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page, an unverified-catalogue answer, or the coded failure.</returns>
    public async Task<Result<MarketplaceCatalogueDto>> HandleAsync(
        ListCatalogueQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The configured channel when options are usable; otherwise the documented default, so a switched-off
        // or misconfigured marketplace still reaches the client and is refused there with its own code.
        var configured = _options.Read();
        var channel = query.Channel ?? (configured.IsSuccess ? configured.Value.Channel : MarketplaceOptions.DefaultChannel);

        var page = await _client.ListCatalogueAsync(
            channel, MarketplaceDisplayNames.Locale, query.Search, query.Cursor, cancellationToken);

        if (!page.IsSuccess)
        {
            if (!CatalogueVerificationPolicy.IsVerificationFailure(page.Error!))
            {
                return Result<MarketplaceCatalogueDto>.Fail(page.Error!);
            }

            await _journal.RecordVerificationFailedAsync(
                page.Error!, query.IpAddress, query.UserAgent, cancellationToken);
            return Result<MarketplaceCatalogueDto>.Ok(
                new MarketplaceCatalogueDto([], NextCursor: null, Verified: false, FetchedAt: null, _names.Sentence(page.Error!)));
        }

        var context = await _eligibility.LoadContextAsync(cancellationToken);
        var items = new List<MarketplaceModuleDto>(page.Value.Items.Count);
        foreach (var module in page.Value.Items)
        {
            items.Add(await _projector.ProjectModuleAsync(context, module, cancellationToken));
        }

        return Result<MarketplaceCatalogueDto>.Ok(new MarketplaceCatalogueDto(
            items, page.Value.NextCursor, Verified: true, page.Value.SignedAt, UnverifiedMessage: null));
    }
}
