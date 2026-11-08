namespace Maran.Modules.Mail.Queries.GetMailDomainDnsRecords;

/// <summary>Reads the DNS records a mail domain needs.</summary>
/// <param name="MailDomainId">The domain; comes from the route.</param>
public sealed record GetMailDomainDnsRecordsQuery(Guid MailDomainId);
