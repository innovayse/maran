using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Models;
using Maran.Modules.Accounts.Resources;
using Maran.Sdk.Extensions;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Mappers;

/// <summary>
/// Restates a <see cref="Reseller"/> and its <see cref="ResellerEnvelope"/> as the wire shapes a screen
/// reads, and a validated envelope sent by an administrator as the domain value.
/// </summary>
/// <remarks>
/// It translates and never decides (rules/csharp.md "A mapper translates; it never decides"): the
/// usage arrives already measured, and the envelope arrives already validated by
/// <c>ResellerEnvelopeDtoValidator</c>, so the domain value's own guards can only be reached by a bug.
/// </remarks>
public static class ResellerMapper
{
    /// <summary>The prefix of a reseller status's name in <c>DisplayNames</c>.</summary>
    private const string StatusKeyPrefix = "ResellerStatus";

    /// <summary>Names a reseller's state in the request's culture.</summary>
    /// <param name="status">The state to name.</param>
    /// <param name="displayNames">Resolves the name in the request's culture.</param>
    /// <returns>The state's name for the caller to read.</returns>
    public static string StatusNameOf(ResellerStatus status, IStringLocalizer<DisplayNames> displayNames)
    {
        return displayNames[StatusKeyPrefix + status].Value;
    }

    /// <summary>Projects one reseller onto the list's wire shape.</summary>
    /// <param name="reseller">The reseller to project.</param>
    /// <param name="usage">What the reseller owns right now.</param>
    /// <param name="displayNames">Resolves the state's name in the request's culture.</param>
    /// <returns>The reseller's list row.</returns>
    public static ResellerDto ToDto(Reseller reseller, ResellerUsage usage, IStringLocalizer<DisplayNames> displayNames)
    {
        return new ResellerDto(
            reseller.Id,
            reseller.Name,
            reseller.ContactEmail,
            reseller.Status,
            StatusNameOf(reseller.Status, displayNames),
            reseller.CreatedAt,
            usage.AccountCount,
            usage.PlanCount,
            usage.DiskAllocatedMb);
    }

    /// <summary>Projects one reseller onto the detail wire shape, envelope included.</summary>
    /// <param name="reseller">The reseller to project.</param>
    /// <param name="usage">What the reseller owns right now.</param>
    /// <param name="displayNames">Resolves the state's name in the request's culture.</param>
    /// <returns>The reseller's detail view.</returns>
    public static ResellerDetailDto ToDetailDto(
        Reseller reseller,
        ResellerUsage usage,
        IStringLocalizer<DisplayNames> displayNames)
    {
        return new ResellerDetailDto(
            reseller.Id,
            reseller.Name,
            reseller.ContactEmail,
            reseller.Status,
            StatusNameOf(reseller.Status, displayNames),
            reseller.CreatedAt,
            usage.AccountCount,
            usage.PlanCount,
            usage.DiskAllocatedMb,
            reseller.Language,
            ToEnvelopeDto(reseller.Envelope));
    }

    /// <summary>Projects an envelope onto the wire.</summary>
    /// <param name="envelope">The envelope to project.</param>
    /// <returns>The envelope's wire shape.</returns>
    public static ResellerEnvelopeDto ToEnvelopeDto(ResellerEnvelope envelope)
    {
        return new ResellerEnvelopeDto(
            envelope.MaxAccounts,
            envelope.DiskPoolMb,
            envelope.MaxPlans,
            envelope.MaxDiskQuotaMb,
            envelope.MaxSites,
            envelope.MaxDatabases,
            envelope.MaxSftpUsers,
            envelope.MaxFtpUsers,
            envelope.MaxCronEntries,
            envelope.MaxPhpWorkersPerPool,
            envelope.Features.Members(),
            RuntimeLimitsMapper.ToDto(envelope.RuntimeLimits.CpuQuotaPercent),
            RuntimeLimitsMapper.ToDto(envelope.RuntimeLimits.MemoryMaxMb),
            RuntimeLimitsMapper.ToDto(envelope.RuntimeLimits.MaxProcesses));
    }

    /// <summary>Reads a validated wire envelope as the domain value.</summary>
    /// <param name="envelope">The wire envelope, already validated.</param>
    /// <returns>The envelope the reseller is sold.</returns>
    public static ResellerEnvelope ToDomain(ResellerEnvelopeDto envelope)
    {
        return ResellerEnvelope.Of(
            envelope.MaxAccounts,
            envelope.DiskPoolMb,
            envelope.MaxPlans,
            envelope.MaxDiskQuotaMb,
            envelope.MaxSites,
            envelope.MaxDatabases,
            envelope.MaxSftpUsers,
            envelope.MaxFtpUsers,
            envelope.MaxCronEntries,
            envelope.MaxPhpWorkersPerPool,
            envelope.Features.ToSet(),
            RuntimeLimits.Of(
                LimitOf(envelope.CpuQuotaPercent),
                LimitOf(envelope.MemoryMaxMb),
                LimitOf(envelope.MaxProcesses)));
    }

    /// <summary>Reads one validated wire ceiling as the stored one.</summary>
    /// <remarks>
    /// The same two-case reading <see cref="RuntimeLimitsMapper"/> applies to a plan's ceilings, kept
    /// here because that type's reading of a single ceiling is private to it and takes a plan
    /// definition as a whole.
    /// </remarks>
    /// <param name="ceiling">The wire ceiling, already validated.</param>
    /// <returns>The stored ceiling.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a shape the validator would have refused.</exception>
    private static ResourceLimit LimitOf(ResourceCeilingDto ceiling)
    {
        return ceiling switch
        {
            LimitedResourceCeilingDto limited => ResourceLimit.Limited(limited.Maximum),
            UnlimitedResourceCeilingDto => ResourceLimit.Unlimited(),
            _ => throw new ArgumentOutOfRangeException(nameof(ceiling), ceiling, "Unknown ceiling shape."),
        };
    }
}
