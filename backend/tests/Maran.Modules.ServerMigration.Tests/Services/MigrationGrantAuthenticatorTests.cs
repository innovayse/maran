using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Services;
using Maran.Modules.ServerMigration.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Utilities.Tokens;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.ServerMigration.Tests.Services;

/// <summary>What the exporting panel's grant check accepts and what it turns away (issue #127).</summary>
public sealed class MigrationGrantAuthenticatorTests
{
    /// <summary>The moment the checks below are made.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The caller's address.</summary>
    private const string Caller = "198.51.100.7";

    /// <summary>Builds a database holding one live export and one grant for it.</summary>
    /// <param name="name">A name for the in-memory database.</param>
    /// <param name="allowedFrom">The grant's address restriction, or <c>null</c>.</param>
    /// <returns>The authenticator, the database, the token and the grant.</returns>
    private static (MigrationGrantAuthenticator Authenticator, ServerMigrationDbContext Database, string Token, ExportGrant Grant) Build(
        string name,
        string? allowedFrom = null)
    {
        var database = InMemoryMigrationDatabase.Open(name);
        var export = new AccountExport(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "alice",
            new MigrationExportArtifact(Guid.NewGuid(), 10, new string('c', 64), 0),
            "[]",
            Guid.NewGuid(),
            Now.AddMinutes(-5),
            TimeSpan.FromDays(7));
        var (token, digest) = ExportGrantTokenHasher.Mint();
        var grant = new ExportGrant(Guid.NewGuid(), export.Id, digest, allowedFrom, Now.AddMinutes(-1), TimeSpan.FromMinutes(30));
        database.AccountExports.Add(export);
        database.ExportGrants.Add(grant);
        database.SaveChanges();

        return (new MigrationGrantAuthenticator(database, NullLogger<MigrationGrantAuthenticator>.Instance), database, token, grant);
    }

    /// <summary>The accepting control: a live grant presented from an allowed address authenticates, naming its export.</summary>
    [Fact]
    public async Task A_live_grant_authenticates_and_names_its_export()
    {
        var (authenticator, _, token, grant) = Build(nameof(A_live_grant_authenticates_and_names_its_export));

        var answer = await authenticator.AuthenticateAsync(token, Caller, Now, CancellationToken.None);

        var authenticated = Assert.IsType<MigrationGrantAuthenticated>(answer);
        Assert.Equal(grant.Id, authenticated.GrantId);
        Assert.Equal(grant.ExportId, authenticated.ExportId);
    }

    /// <summary>A token that matches nothing, or is the wrong shape, is one denial.</summary>
    [Fact]
    public async Task An_unknown_or_malformed_token_is_denied()
    {
        var (authenticator, _, _, _) = Build(nameof(An_unknown_or_malformed_token_is_denied));
        var (stranger, _) = ExportGrantTokenHasher.Mint();

        Assert.IsType<MigrationGrantDenied>(await authenticator.AuthenticateAsync(stranger, Caller, Now, CancellationToken.None));
        Assert.IsType<MigrationGrantDenied>(await authenticator.AuthenticateAsync("short", Caller, Now, CancellationToken.None));
        Assert.IsType<MigrationGrantDenied>(await authenticator.AuthenticateAsync(new string('x', 5000), Caller, Now, CancellationToken.None));
    }

    /// <summary>An expired grant, a revoked one and one whose artifact was already read are denied.</summary>
    [Fact]
    public async Task An_expired_revoked_or_used_grant_is_denied()
    {
        var (authenticator, database, token, grant) = Build(nameof(An_expired_revoked_or_used_grant_is_denied));

        Assert.IsType<MigrationGrantDenied>(await authenticator.AuthenticateAsync(token, Caller, Now.AddHours(2), CancellationToken.None));

        grant.ConsumeArtifact(Now);
        database.SaveChanges();
        Assert.IsType<MigrationGrantDenied>(await authenticator.AuthenticateAsync(token, Caller, Now, CancellationToken.None));

        var (revokedAuth, revokedDb, revokedToken, revoked) = Build(nameof(An_expired_revoked_or_used_grant_is_denied) + "-revoked");
        revoked.Revoke(Now);
        revokedDb.SaveChanges();
        Assert.IsType<MigrationGrantDenied>(await revokedAuth.AuthenticateAsync(revokedToken, Caller, Now, CancellationToken.None));
    }

    /// <summary>A grant tied to an address range is denied from outside it and accepted from inside it.</summary>
    [Fact]
    public async Task A_grant_tied_to_a_range_is_denied_from_outside_it()
    {
        var (authenticator, _, token, _) = Build(nameof(A_grant_tied_to_a_range_is_denied_from_outside_it), "203.0.113.0/24");

        Assert.IsType<MigrationGrantDenied>(await authenticator.AuthenticateAsync(token, Caller, Now, CancellationToken.None));
        Assert.IsType<MigrationGrantAuthenticated>(await authenticator.AuthenticateAsync(token, "203.0.113.50", Now, CancellationToken.None));
    }

    /// <summary>A grant that outlives its export opens nothing; the grant is live, so the export alone is what refuses.</summary>
    [Fact]
    public async Task A_live_grant_for_an_expired_export_is_denied()
    {
        var (authenticator, database, _, _) = Build(nameof(A_live_grant_for_an_expired_export_is_denied));
        var export = database.AccountExports.Single();
        var (token, digest) = ExportGrantTokenHasher.Mint();
        var longGrant = new ExportGrant(Guid.NewGuid(), export.Id, digest, null, Now, TimeSpan.FromDays(60));
        database.ExportGrants.Add(longGrant);
        database.SaveChanges();
        var afterExpiry = export.ExpiresAt.AddMinutes(1);

        Assert.True(longGrant.IsLive(afterExpiry));
        Assert.False(export.IsAvailable(afterExpiry));
        Assert.IsType<MigrationGrantDenied>(await authenticator.AuthenticateAsync(token, Caller, afterExpiry, CancellationToken.None));
        Assert.IsType<MigrationGrantAuthenticated>(await authenticator.AuthenticateAsync(token, Caller, Now, CancellationToken.None));
    }
}
