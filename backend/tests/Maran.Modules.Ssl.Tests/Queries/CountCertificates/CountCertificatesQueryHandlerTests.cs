using Maran.Modules.Ssl.Queries.CountCertificates;
using Maran.Modules.Ssl.Tests.TestSupport;

namespace Maran.Modules.Ssl.Tests.Queries.CountCertificates;

/// <summary>Behavioral contract of <see cref="CountCertificatesQueryHandler"/>.</summary>
public sealed class CountCertificatesQueryHandlerTests
{
    /// <summary>A fixed instant, so nothing here reads the ambient clock.</summary>
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A server holding no certificate counts zero.</summary>
    [Fact]
    public async Task A_server_with_no_certificate_counts_zero()
    {
        await using var dbContext = SslTestContext.Create(FakeCurrentUser.Admin());

        var result = await new CountCertificatesQueryHandler(dbContext)
            .HandleAsync(new CountCertificatesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
    }

    /// <summary>
    /// An administrator's count is every certificate on the server, whichever account holds it and
    /// however far from expiry, and not the size of one list page.
    /// </summary>
    [Fact]
    public async Task An_administrators_count_is_every_certificate_on_the_server()
    {
        await using var dbContext = SslTestContext.Create(FakeCurrentUser.Admin());
        dbContext.Certificates.AddRange(
            SslTestContext.Certificate(Guid.NewGuid(), "one.example.com", Now.AddDays(80)),
            SslTestContext.Certificate(Guid.NewGuid(), "two.example.com", Now.AddDays(-3)),
            SslTestContext.Certificate(Guid.NewGuid(), "three.example.com", Now.AddDays(10)));
        await dbContext.SaveChangesAsync();

        var result = await new CountCertificatesQueryHandler(dbContext)
            .HandleAsync(new CountCertificatesQuery(), CancellationToken.None);

        Assert.Equal(3, result.Value);
    }
}
