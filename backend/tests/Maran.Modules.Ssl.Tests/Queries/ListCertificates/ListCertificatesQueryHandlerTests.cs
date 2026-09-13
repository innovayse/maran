using Maran.Modules.Ssl.Queries.ListCertificates;
using Maran.Modules.Ssl.Tests.TestSupport;

namespace Maran.Modules.Ssl.Tests.Queries.ListCertificates;

/// <summary>The list query, driven against a database holding two customers' rows.</summary>
public sealed class ListCertificatesQueryHandlerTests
{
    /// <summary>A fixed instant, so nothing here reads the ambient clock.</summary>
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A customer sees only their own certificates soonest expiry first.</summary>
    [Fact]
    public async Task A_customer_sees_only_their_own_certificates_soonest_expiry_first()
    {
        var mine = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();

        await using (var seeding = SslTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Certificates.Add(SslTestContext.Certificate(mine, "late.example.com", Now.AddDays(80)));
            seeding.Certificates.Add(SslTestContext.Certificate(mine, "soon.example.com", Now.AddDays(10)));
            seeding.Certificates.Add(SslTestContext.Certificate(Guid.NewGuid(), "theirs.example.com", Now));
            await seeding.SaveChangesAsync();
        }

        await using var reading = SslTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var result = await new ListCertificatesQueryHandler(reading)
            .HandleAsync(new ListCertificatesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["soon.example.com", "late.example.com"], result.Value.Select(certificate =>
        {
            return certificate.Domain;
        }));
    }
}
