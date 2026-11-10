using Maran.Modules.Marketplace.Common;
using Maran.Modules.Marketplace.Domain.Policies;
using Maran.Modules.Marketplace.Interfaces;
using Maran.Modules.Marketplace.Mappers;
using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Options;
using Maran.Modules.Marketplace.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Marketplace.Queries.CheckUpdates;

/// <summary>
/// Handles <see cref="CheckUpdatesQuery"/>: sends the marketplace the id, version and digest of each module
/// it installed and nothing else, and reports what came back with whether each update can start now.
/// </summary>
/// <remarks>
/// <para>
/// Only modules the panel itself staged (those with a ledger row) are sent; a module that arrived another
/// way is not the marketplace's to update. An unverified answer is a 200 with <c>verified: false</c>, as for
/// the catalogue, and is journalled. A verified answer replaces what <see cref="ObservedRevocations"/>
/// remembers, so the installed list can mark withdrawn modules without calling the marketplace again.
/// </para>
/// </remarks>
public sealed class CheckUpdatesQueryHandler
{
    /// <summary>The marketplace client.</summary>
    private readonly IMarketplaceClient _client;

    /// <summary>The installed modules.</summary>
    private readonly InstalledModuleReader _installed;

    /// <summary>The validated options, for the default channel.</summary>
    private readonly MarketplaceOptionsReader _options;

    /// <summary>The eligibility rules.</summary>
    private readonly InstallEligibility _eligibility;

    /// <summary>What the last check found withdrawn.</summary>
    private readonly ObservedRevocations _revocations;

    /// <summary>The words, in the caller's language.</summary>
    private readonly MarketplaceDisplayNames _names;

    /// <summary>The audit journal.</summary>
    private readonly MarketplaceAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="client">The marketplace client.</param>
    /// <param name="installed">The installed modules.</param>
    /// <param name="options">The validated options, for the default channel.</param>
    /// <param name="eligibility">The eligibility rules.</param>
    /// <param name="revocations">What the last check found withdrawn.</param>
    /// <param name="names">The words, in the caller's language.</param>
    /// <param name="journal">The audit journal.</param>
    public CheckUpdatesQueryHandler(
        IMarketplaceClient client,
        InstalledModuleReader installed,
        MarketplaceOptionsReader options,
        InstallEligibility eligibility,
        ObservedRevocations revocations,
        MarketplaceDisplayNames names,
        MarketplaceAuditJournal journal)
    {
        _client = client;
        _installed = installed;
        _options = options;
        _eligibility = eligibility;
        _revocations = revocations;
        _names = names;
        _journal = journal;
    }

    /// <summary>Checks for updates.</summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What the check found, an unverified-answer result, or the coded failure.</returns>
    public async Task<Result<ModuleUpdatesDto>> HandleAsync(
        CheckUpdatesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var staged = (await _installed.ReadAsync(cancellationToken))
            .Where(facts => { return facts.Package is not null && facts.Loaded.State != ModuleLifecycleState.Removed; })
            .ToList();
        var references = staged
            .Select(facts => { return new InstalledModuleReference(facts.Loaded.ModuleId, facts.Loaded.Version, facts.Package!.PackageSha256); })
            .ToList();

        var configured = _options.Read();
        var channel = configured.IsSuccess ? configured.Value.Channel : MarketplaceOptions.DefaultChannel;

        var report = await _client.CheckUpdatesAsync(references, channel, MarketplaceDisplayNames.Locale, cancellationToken);
        if (!report.IsSuccess)
        {
            if (!CatalogueVerificationPolicy.IsVerificationFailure(report.Error!))
            {
                return Result<ModuleUpdatesDto>.Fail(report.Error!);
            }

            await _journal.RecordVerificationFailedAsync(
                report.Error!, query.IpAddress, query.UserAgent, cancellationToken);
            return Result<ModuleUpdatesDto>.Ok(
                new ModuleUpdatesDto([], Verified: false, CheckedAt: null, _names.Sentence(report.Error!)));
        }

        _revocations.Replace(report.Value.Items.Where(notice => { return notice.Revoked; }).Select(notice => { return notice.ModuleId; }));

        var context = await _eligibility.LoadContextAsync(cancellationToken);
        var items = new List<ModuleUpdateDto>();
        foreach (var notice in report.Value.Items)
        {
            var running = staged.FirstOrDefault(facts => { return facts.Loaded.ModuleId == notice.ModuleId; });
            if (running is null)
            {
                continue;
            }

            var refusal = await _eligibility.RefuseUpdateOfAsync(context, notice, cancellationToken);

            // No newer version on offer is not a reason to say anything: the screen shows "up to date".
            var reason = notice.Latest is null ? null : _names.SentenceOrNull(refusal);
            items.Add(ModuleUpdateMapper.ToDto(notice, running.Loaded.Version, refusal, reason));
        }

        return Result<ModuleUpdatesDto>.Ok(
            new ModuleUpdatesDto(items, Verified: true, report.Value.SignedAt, UnverifiedMessage: null));
    }
}
