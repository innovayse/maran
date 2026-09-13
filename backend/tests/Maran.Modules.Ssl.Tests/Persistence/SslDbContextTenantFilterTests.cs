using Maran.Modules.Ssl.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Ssl.Tests.Persistence;

/// <summary>
/// The Ssl module's tenant query filter, tested as the thing it is: one database holding two
/// customers' rows, and two contexts that must not see each other's (spec §8). Seeding through a
/// separate database per tenant would let the setup do the separating and the filter could be
/// deleted with every test still green.
/// </summary>
public sealed class SslDbContextTenantFilterTests
{
    /// <summary>A fixed expiry, so nothing here reads the ambient clock.</summary>
    private static readonly DateTimeOffset Expiry = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A customer sees only their own certificates.</summary>
    [Fact]
    public async Task A_customer_sees_only_their_own_certificates()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();

        await using (var seeding = SslTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Certificates.Add(SslTestContext.Certificate(mine, "mine.example.com", Expiry));
            seeding.Certificates.Add(SslTestContext.Certificate(theirs, "theirs.example.com", Expiry));
            await seeding.SaveChangesAsync();
        }

        await using var reading = SslTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var visible = await reading.Certificates.ToListAsync();

        Assert.Equal(["mine.example.com"], visible.Select(certificate =>
        {
            return certificate.Domain;
        }));
    }

    /// <summary>Another customers certificate is not found rather than forbidden.</summary>
    [Fact]
    public async Task Another_customers_certificate_is_not_found_rather_than_forbidden()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var theirCertificate = SslTestContext.Certificate(theirs, "theirs.example.com", Expiry);

        await using (var seeding = SslTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Certificates.Add(theirCertificate);
            await seeding.SaveChangesAsync();
        }

        await using var reading = SslTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var found = await reading.Certificates
            .FirstOrDefaultAsync(
                certificate => certificate.Id == theirCertificate.Id);

        // Not found, so the handler above it answers 404 — a 403 would confirm the row exists.
        Assert.Null(found);
    }

    /// <summary>An administrator sees every accounts certificates.</summary>
    [Fact]
    public async Task An_administrator_sees_every_accounts_certificates()
    {
        var database = Guid.NewGuid().ToString();

        await using (var seeding = SslTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Certificates.Add(SslTestContext.Certificate(Guid.NewGuid(), "one.example.com", Expiry));
            seeding.Certificates.Add(SslTestContext.Certificate(Guid.NewGuid(), "two.example.com", Expiry));
            await seeding.SaveChangesAsync();
        }

        await using var reading = SslTestContext.Create(FakeCurrentUser.Admin(), database);

        Assert.Equal(2, await reading.Certificates.CountAsync());
    }
}
