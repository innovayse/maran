using Maran.Modules.Mail.Domain.Enums;

namespace Maran.Modules.Mail.Domain.ValueObjects;

/// <summary>
/// One DNS record a mail domain needs published for its mail to be delivered, signed and trusted.
/// </summary>
/// <remarks>
/// A value, computed from the server's hostname, the domain and its keys and never stored: the panel
/// does not publish DNS, it tells the customer or operator what to publish (and a later DNS module reads
/// the same values). Computing on every read means a record can never disagree with the key it describes.
/// </remarks>
/// <param name="Type">The record type.</param>
/// <param name="Name">The owner name relative to the domain's zone: <c>@</c> for the apex, otherwise a label path.</param>
/// <param name="Value">The record's data.</param>
/// <param name="Priority">The preference for an MX record; <c>null</c> for any other type.</param>
/// <param name="TtlSeconds">The suggested time to live in seconds.</param>
/// <param name="Purpose">What the record is for.</param>
public sealed record MailDnsRecord(
    MailDnsRecordType Type,
    string Name,
    string Value,
    int? Priority,
    int TtlSeconds,
    MailDnsPurpose Purpose);
