using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Domain.ValueObjects;
using Maran.Modules.Mail.Mappers;
using Maran.Modules.Mail.Tests.TestSupport;

namespace Maran.Modules.Mail.Tests.Mappers;

/// <summary>How a mail domain and a DNS record are restated for the wire.</summary>
public sealed class MailDomainMapperTests
{
    /// <summary>The instant every domain in these tests was created at.</summary>
    private static readonly DateTimeOffset Created = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A domain with no active key reads with an empty selector rather than failing.</summary>
    [Fact]
    public void A_domain_with_no_active_key_has_an_empty_selector()
    {
        var domain = new MailDomain(Guid.NewGuid(), Guid.NewGuid(), "example.com", Guid.NewGuid(), Created);

        var dto = MailDomainMapper.From(
            domain, new Dictionary<Guid, string>(), MailDomainStatus.Active, 0, new DomainGroupDisplayNames());

        Assert.Equal(string.Empty, dto.DkimSelector);
        Assert.Equal(string.Empty, dto.OwnerName);
    }

    /// <summary>An MX record is sent as MX and a TXT record as TXT.</summary>
    [Fact]
    public void Record_types_are_sent_as_their_upper_case_names()
    {
        var names = new DomainGroupDisplayNames();
        var mx = new MailDnsRecord(MailDnsRecordType.Mx, "@", "mail.example.com", 10, 3600, MailDnsPurpose.Delivery);
        var txt = new MailDnsRecord(MailDnsRecordType.Txt, "_dmarc", "v=DMARC1; p=none", null, 3600, MailDnsPurpose.Dmarc);

        Assert.Equal("MX", MailDomainMapper.From(mx, names).Type);
        Assert.Equal("TXT", MailDomainMapper.From(txt, names).Type);
    }
}
