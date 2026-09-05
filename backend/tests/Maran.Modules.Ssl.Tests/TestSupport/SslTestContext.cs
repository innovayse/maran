using Maran.Modules.Ssl.Domain.Entities;
using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// Builds isolated <see cref="SslDbContext"/> instances for a named tenant, plus the certificates to
/// seed them with. Each context gets its own uniquely-named in-memory database unless a caller passes
/// a shared name, which is what an isolation test needs: two contexts, two principals, ONE database,
/// so the only thing separating the rows is the query filter under test.
/// </summary>
public static class SslTestContext
{
    /// <summary>Creates a context over a fresh database, seen as <paramref name="currentUser"/>.</summary>
    /// <param name="currentUser">The principal whose tenant scope the context is bound to.</param>
    /// <param name="databaseName">The in-memory database to open; a fresh one when omitted.</param>
    /// <param name="interceptor">An optional interceptor, used to simulate a database refusing a write.</param>
    public static SslDbContext Create(
        ICurrentUser currentUser,
        string? databaseName = null,
        IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<SslDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString());

        if (interceptor is not null)
        {
            builder = builder.AddInterceptors(interceptor);
        }

        var options = builder.Options;
        return new SslDbContext(options, currentUser, new PassThroughEncryptionService());
    }

    /// <summary>Builds a certificate row.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="domain">The certificate's domain.</param>
    /// <param name="notAfter">When it expires.</param>
    /// <param name="source">Where it came from.</param>
    /// <param name="siteId">The site it belongs to; a fresh identity when omitted.</param>
    public static Certificate Certificate(
        Guid accountId,
        string domain,
        DateTimeOffset notAfter,
        CertificateSource source = CertificateSource.Acme,
        Guid? siteId = null)
    {
        return new Certificate(
            Guid.NewGuid(),
            accountId,
            siteId ?? Guid.NewGuid(),
            domain,
            source,
            notAfter,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }
}
