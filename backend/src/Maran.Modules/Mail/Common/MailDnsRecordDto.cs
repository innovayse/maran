namespace Maran.Modules.Mail.Common;

/// <summary>One DNS record a mail domain needs, with the sentence that says what it is for.</summary>
/// <param name="Type">The record type, <c>MX</c> or <c>TXT</c>.</param>
/// <param name="Name">The owner name relative to the zone: <c>@</c>, <c>&lt;selector&gt;._domainkey</c> or <c>_dmarc</c>.</param>
/// <param name="Value">The record's data.</param>
/// <param name="Priority">The MX preference, or <c>null</c> for a record that has none.</param>
/// <param name="PurposeName">The localized name of what the record is for.</param>
public sealed record MailDnsRecordDto(string Type, string Name, string Value, int? Priority, string PurposeName);
