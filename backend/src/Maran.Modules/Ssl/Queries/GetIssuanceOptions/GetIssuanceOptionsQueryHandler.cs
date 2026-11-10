using Maran.Modules.Ssl.Common;
using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Mappers;
using Maran.Modules.Ssl.Resources;
using Maran.Modules.Ssl.Services;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ssl.Queries.GetIssuanceOptions;

/// <summary>Handles <see cref="GetIssuanceOptionsQuery"/>.</summary>
/// <remarks>
/// Ownership is established first, through the tenant-scoped site directory, so a domain another account
/// hosts answers exactly as a domain nobody hosts does (rules/security.md item 6). The wildcard answer is the
/// same <see cref="WildcardAvailabilityCheck"/> the issuance command refuses on, so the checkbox the screen
/// shows and the request the panel accepts cannot disagree.
/// </remarks>
public sealed class GetIssuanceOptionsQueryHandler
{
    /// <summary>The tenant-scoped window onto the sites this caller owns.</summary>
    private readonly ISiteDirectory _sites;

    /// <summary>Whether a wildcard can be ordered for the domain.</summary>
    private readonly WildcardAvailabilityCheck _wildcards;

    /// <summary>The panel's one resolution from an error code to its sentence.</summary>
    private readonly IErrorTextProvider _errorTexts;

    /// <summary>Creates the handler.</summary>
    /// <param name="sites">The tenant-scoped window onto the sites this caller owns.</param>
    /// <param name="wildcards">Answers whether a wildcard can be ordered for the domain.</param>
    /// <param name="errorTexts">The panel's error-message resolution, composed from every module's tables.</param>
    public GetIssuanceOptionsQueryHandler(
        ISiteDirectory sites,
        WildcardAvailabilityCheck wildcards,
        IErrorTextProvider errorTexts)
    {
        _sites = sites;
        _wildcards = wildcards;
        _errorTexts = errorTexts;
    }

    /// <summary>Describes what can be issued for the domain.</summary>
    /// <param name="query">The validated domain.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The options, or <c>SiteNotFound</c> for a domain the caller does not host.</returns>
    public async Task<Result<IssuanceOptionsDto>> HandleAsync(
        GetIssuanceOptionsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var site = await _sites.FindByDomainAsync(query.Domain, cancellationToken);
        if (site is null)
        {
            return Result<IssuanceOptionsDto>.Fail(Error.Of(nameof(ErrorMessages.SiteNotFound), ErrorType.NotFound));
        }

        var availability = await _wildcards.CheckAsync(site.AccountId, query.Domain, cancellationToken);
        if (availability == WildcardAvailability.Available)
        {
            return Result<IssuanceOptionsDto>.Ok(new IssuanceOptionsDto(query.Domain, true, null, null));
        }

        var refusal = WildcardRefusalMapper.From(availability);
        return Result<IssuanceOptionsDto>.Ok(new IssuanceOptionsDto(
            query.Domain,
            false,
            refusal.Code,
            _errorTexts.Resolve(refusal.Code)));
    }
}
