using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;

namespace Maran.Modules.Accounts.Queries.GetProvisioningInfo;

/// <summary>
/// Handles <see cref="GetProvisioningInfoQuery"/> by reading the reseller the caller acts for, with its
/// envelope and what it owns.
/// </summary>
/// <remarks>
/// The reseller id comes from <see cref="ResellerScope.CallerResellerId"/> and from nowhere else.
/// A caller that acts for no reseller owns none and gets the same <c>ResellerNotFound</c> as a reseller
/// whose row is gone, so the answer says nothing about which. Nothing about the host is read.
/// </remarks>
public sealed class GetProvisioningInfoQueryHandler
{
    /// <summary>The revision of the provisioning API. It only grows, and only by additions inside a major version.</summary>
    public const int ApiRevision = 1;

    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>States which reseller the caller acts for.</summary>
    private readonly ResellerScope _scope;

    /// <summary>Measures what the reseller owns.</summary>
    private readonly ResellerUsageReader _usage;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="scope">States which reseller the caller acts for.</param>
    /// <param name="usage">Measures what the reseller owns.</param>
    public GetProvisioningInfoQueryHandler(AccountsDbContext dbContext, ResellerScope scope, ResellerUsageReader usage)
    {
        _dbContext = dbContext;
        _scope = scope;
        _usage = usage;
    }

    /// <summary>Returns what the caller's reseller may provision, or a typed failure when the caller has none.</summary>
    /// <param name="query">The empty request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The provisioning info, or <c>ResellerNotFound</c>.</returns>
    public async Task<Result<ProvisioningInfoDto>> HandleAsync(
        GetProvisioningInfoQuery query,
        CancellationToken cancellationToken)
    {
        if (_scope.CallerResellerId is not { } resellerId)
        {
            return Result<ProvisioningInfoDto>.Fail(Error.Of(nameof(ErrorMessages.ResellerNotFound), ErrorType.NotFound));
        }

        var reseller = await _dbContext.Resellers
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == resellerId, cancellationToken);
        if (reseller is null)
        {
            return Result<ProvisioningInfoDto>.Fail(Error.Of(nameof(ErrorMessages.ResellerNotFound), ErrorType.NotFound));
        }

        var usage = await _usage.ReadAsync(reseller.Id, cancellationToken);
        return Result<ProvisioningInfoDto>.Ok(new ProvisioningInfoDto(
            ApiRevision,
            usage.AccountCount,
            usage.PlanCount,
            usage.DiskAllocatedMb,
            ResellerMapper.ToEnvelopeDto(reseller.Envelope)));
    }
}
