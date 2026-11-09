namespace Maran.Modules.Dns.Commands.DeleteRecord;

/// <summary>Removes one record from a zone.</summary>
/// <param name="ZoneId">The zone. A row identifier; another account's zone answers "not found".</param>
/// <param name="RecordId">The record, which must belong to that zone.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record DeleteRecordCommand(Guid ZoneId, Guid RecordId, string IpAddress, string UserAgent);
