using Maran.Modules.Mail.Queries.GetMailDomainDnsRecords;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Mail.Tests.Queries.GetMailDomainDnsRecords;

/// <summary>The DNS records a mail domain publishes: their shape, their localized purposes and who may read them.</summary>
public sealed class GetMailDomainDnsRecordsQueryHandlerTests
{
    /// <summary>The account that owns the domain in these tests.</summary>
    private static readonly Guid AccountId = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>Another account whose context must not read the first one's records.</summary>
    private static readonly Guid OtherAccountId = new("0f5c2a13-8b46-4d7e-a1c9-25e7b4d6f083");

    /// <summary>The records are MX, SPF, the DKIM key and DMARC with zone-relative names and named purposes.</summary>
    [Fact]
    public async Task The_records_are_mx_spf_dkim_and_dmarc_with_zone_relative_names()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        await context.SeedEnabledServerAsync();
        var domain = await context.SeedDomainAsync(AccountId, "example.com");

        var result = await DomainGroupHandlers.DnsRecords(context).HandleAsync(
            new GetMailDomainDnsRecordsQuery(domain.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var records = result.Value;
        Assert.Equal(4, records.Count);

        Assert.Equal("MX", records[0].Type);
        Assert.Equal("@", records[0].Name);
        Assert.Equal("mail.example.com", records[0].Value);
        Assert.Equal(10, records[0].Priority);
        Assert.Equal("MailDnsRecordPurposeDelivery", records[0].PurposeName);

        Assert.Equal("TXT", records[1].Type);
        Assert.Equal("v=spf1 mx ~all", records[1].Value);
        Assert.Null(records[1].Priority);
        Assert.Equal("MailDnsRecordPurposeSpf", records[1].PurposeName);

        Assert.Equal("m20261107-1._domainkey", records[2].Name);
        Assert.Equal("v=DKIM1; k=rsa; p=PUBLICKEY0", records[2].Value);
        Assert.Equal("MailDnsRecordPurposeDkimKey", records[2].PurposeName);

        Assert.Equal("_dmarc", records[3].Name);
        Assert.Equal("MailDnsRecordPurposeDmarc", records[3].PurposeName);
    }

    /// <summary>A server with no settings row reads as mail not enabled.</summary>
    [Fact]
    public async Task A_server_with_no_settings_reads_as_not_enabled()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        var domain = await context.SeedDomainAsync(AccountId, "example.com");

        var result = await DomainGroupHandlers.DnsRecords(context).HandleAsync(
            new GetMailDomainDnsRecordsQuery(domain.Id), CancellationToken.None);

        Assert.Equal("MailNotEnabled", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    /// <summary>A domain that does not exist is not found.</summary>
    [Fact]
    public async Task A_domain_that_does_not_exist_is_not_found()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        await context.SeedEnabledServerAsync();

        var result = await DomainGroupHandlers.DnsRecords(context).HandleAsync(
            new GetMailDomainDnsRecordsQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal("MailDomainNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    /// <summary>Another account's domain answers not found; the owner's own context reads the records.</summary>
    [Fact]
    public async Task Another_accounts_domain_answers_not_found()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var owner = new MailTestContext(new CustomerCurrentUser(AccountId), databaseName);
        await owner.SeedEnabledServerAsync();
        var domain = await owner.SeedDomainAsync(AccountId, "example.com");

        using var intruder = new MailTestContext(new CustomerCurrentUser(OtherAccountId), databaseName);
        var refused = await DomainGroupHandlers.DnsRecords(intruder).HandleAsync(
            new GetMailDomainDnsRecordsQuery(domain.Id), CancellationToken.None);
        var accepted = await DomainGroupHandlers.DnsRecords(owner).HandleAsync(
            new GetMailDomainDnsRecordsQuery(domain.Id), CancellationToken.None);

        Assert.Equal("MailDomainNotFound", refused.Error!.Code);
        Assert.True(accepted.IsSuccess);
    }
}
