using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Domain.ValueObjects;

namespace Maran.Modules.Mail.Domain.Policies;

/// <summary>Computes the DNS records a mail domain needs from the server's hostname, the domain and its keys.</summary>
/// <remarks>
/// <para>
/// The defaults are the owner's: SPF <c>~all</c> (a soft fail, so a customer's forgotten sender is
/// marked rather than lost) and DMARC <c>p=none</c> with no report address, so a domain starts by
/// observing nothing it did not ask to observe. A stricter policy is the customer's to publish.
/// </para>
/// <para>
/// A key in the <see cref="DkimKeyState.Retired"/> state yields no record: it signs nothing, so nothing
/// needs to verify against it.
/// </para>
/// </remarks>
public static class MailDnsRecordPolicy
{
    /// <summary>The owner name of a record at the zone's apex, as zone files spell it.</summary>
    private const string ApexName = "@";

    /// <summary>The MX preference of the single exchanger.</summary>
    private const int MxPriority = 10;

    /// <summary>The SPF policy: this domain's own MX hosts may send for it, everything else soft-fails.</summary>
    private const string SpfValue = "v=spf1 mx ~all";

    /// <summary>The DMARC policy: observe only, no report address.</summary>
    private const string DmarcValue = "v=DMARC1; p=none";

    /// <summary>Lists the records for one domain.</summary>
    /// <param name="domain">The mail domain, with its DKIM keys loaded.</param>
    /// <param name="serverHostname">The hostname the mail server presents, which the MX points at.</param>
    /// <returns>The MX, SPF, one DKIM record per published key, and DMARC, in that order.</returns>
    public static IReadOnlyList<MailDnsRecord> For(MailDomain domain, string serverHostname)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentException.ThrowIfNullOrEmpty(serverHostname);

        var records = new List<MailDnsRecord>
        {
            new(
                MailDnsRecordType.Mx,
                ApexName,
                serverHostname,
                MxPriority,
                MailDefaults.DnsTtlSeconds,
                MailDnsPurpose.Delivery),
            new(MailDnsRecordType.Txt, ApexName, SpfValue, null, MailDefaults.DnsTtlSeconds, MailDnsPurpose.Spf),
        };

        foreach (var key in domain.DkimKeys
            .Where(candidate =>
            {
                return candidate.State != DkimKeyState.Retired;
            })
            .OrderBy(candidate =>
            {
                return candidate.CreatedAt;
            }))
        {
            records.Add(new MailDnsRecord(
                MailDnsRecordType.Txt,
                string.Concat(key.Selector, "._domainkey"),
                string.Concat("v=DKIM1; k=rsa; p=", key.PublicKey),
                null,
                MailDefaults.DnsTtlSeconds,
                MailDnsPurpose.DkimKey));
        }

        records.Add(new MailDnsRecord(
            MailDnsRecordType.Txt,
            "_dmarc",
            DmarcValue,
            null,
            MailDefaults.DnsTtlSeconds,
            MailDnsPurpose.Dmarc));

        return records;
    }
}
