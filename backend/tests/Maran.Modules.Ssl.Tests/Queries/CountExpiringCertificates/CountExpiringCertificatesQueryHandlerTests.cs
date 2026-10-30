using Maran.Modules.Ssl.Queries.CountExpiringCertificates;
using Maran.Modules.Ssl.Tests.TestSupport;

namespace Maran.Modules.Ssl.Tests.Queries.CountExpiringCertificates;

/// <summary>Behavioral contract of <see cref="CountExpiringCertificatesQueryHandler"/>.</summary>
public sealed class CountExpiringCertificatesQueryHandlerTests
{
    /// <summary>A fixed instant, so nothing here reads the ambient clock.</summary>
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The count includes what expires at or before the instant, the already expired among it, and
    /// only the caller's own certificates.
    /// </summary>
    [Fact]
    public async Task The_count_includes_what_expires_by_the_instant_already_expired_included_and_only_the_callers_own()
    {
        var mine = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await using (var seeding = SslTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Certificates.AddRange(
                SslTestContext.Certificate(mine, "lapsed.example.com", Now.AddDays(-1)),
                SslTestContext.Certificate(mine, "edge.example.com", Now),
                SslTestContext.Certificate(mine, "later.example.com", Now.AddSeconds(1)),
                SslTestContext.Certificate(Guid.NewGuid(), "theirs.example.com", Now.AddDays(-1)));
            await seeding.SaveChangesAsync();
        }

        await using var reading = SslTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var result = await new CountExpiringCertificatesQueryHandler(reading)
            .HandleAsync(new CountExpiringCertificatesQuery(Now), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value);
    }
}
