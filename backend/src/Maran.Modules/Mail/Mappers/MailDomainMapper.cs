using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Domain.ValueObjects;
using Maran.Modules.Mail.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Mail.Mappers;

/// <summary>Restates mail domains and their DNS records as the shapes the API sends.</summary>
/// <remarks>
/// A mapper translates and never decides: the status arrives already derived (from the account's
/// state, by the handler) and is only named here, and the DKIM selector is read from the domain's
/// active key as the entity itself defines it.
/// </remarks>
public static class MailDomainMapper
{
    /// <summary>The wire type for an MX record.</summary>
    private const string MxType = "MX";

    /// <summary>The wire type for a TXT record.</summary>
    private const string TxtType = "TXT";

    /// <summary>Restates a mail domain as its list row.</summary>
    /// <param name="domain">The domain, with its DKIM keys loaded.</param>
    /// <param name="ownerNames">Account display names by id; an unknown owner reads as empty.</param>
    /// <param name="status">The domain's derived status.</param>
    /// <param name="mailboxCount">How many mailboxes the domain holds.</param>
    /// <param name="names">The module's localized display names.</param>
    /// <returns>The row.</returns>
    public static MailDomainDto From(
        MailDomain domain,
        IReadOnlyDictionary<Guid, string> ownerNames,
        MailDomainStatus status,
        int mailboxCount,
        IStringLocalizer<DisplayNames> names)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(ownerNames);
        ArgumentNullException.ThrowIfNull(names);

        return new MailDomainDto(
            domain.Id,
            domain.AccountId,
            ownerNames.GetValueOrDefault(domain.AccountId, string.Empty),
            domain.Name,
            status,
            names[string.Concat("MailDomainStatus", status.ToString())].Value,
            domain.ActiveKey?.Selector ?? string.Empty,
            mailboxCount,
            domain.CreatedAt);
    }

    /// <summary>Restates a computed DNS record for the wire.</summary>
    /// <param name="record">The record the policy computed.</param>
    /// <param name="names">The module's localized display names.</param>
    /// <returns>The record with its purpose named in the caller's language.</returns>
    public static MailDnsRecordDto From(MailDnsRecord record, IStringLocalizer<DisplayNames> names)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(names);

        var type = record.Type == MailDnsRecordType.Mx ? MxType : TxtType;

        return new MailDnsRecordDto(
            type,
            record.Name,
            record.Value,
            record.Priority,
            names[string.Concat("MailDnsRecordPurpose", record.Purpose.ToString())].Value);
    }
}
