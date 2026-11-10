using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Ssl.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The Ssl migration that adds <c>Certificates."IsWildcard"</c> (issue #161) is additive: a certificate stored
/// before it reads as a plain one and is otherwise untouched, which is also what a release that predates the
/// column keeps seeing.
/// </summary>
/// <remarks>
/// The database is first brought to the schema the PREVIOUS release shipped, a certificate is written the way
/// that release wrote it, and only then is the migration applied. Migrating a fresh database all the way up would
/// show an empty table and prove nothing about the default, which only has an effect on an upgrade. The probe
/// that asks whether the column exists is run BEFORE the migration as well, expecting it absent: a probe that
/// could not see the column would otherwise pass the "present afterwards" assertion for the wrong reason.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class CertificateWildcardMigrationTests : IAsyncLifetime
{
    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The last Ssl migration the previous release shipped.</summary>
    private const string PreviousRelease = "20261105090000_AddCertificateExpiryAlertedAt";

    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public CertificateWildcardMigrationTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    /// <returns>Resolves once this test's database exists.</returns>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    /// <returns>Resolves immediately; the shared server outlives the test.</returns>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>The column is absent in the previous release's schema, present afterwards, and an existing certificate reads as plain.</summary>
    [Fact]
    public async Task An_existing_certificate_reads_as_plain_after_the_column_is_added()
    {
        await using var factory = CreateFactory();
        var id = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<SslDbContext>();
            await dbContext.GetService<IMigrator>().MigrateAsync(PreviousRelease);

            // The probe's own control: before the migration it must find no such column.
            Assert.False(await ColumnExistsAsync(dbContext));
            await StoreCertificateAsync(dbContext, id);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<SslDbContext>();
            await dbContext.Database.MigrateAsync();

            Assert.True(await ColumnExistsAsync(dbContext));
            var row = await dbContext.Certificates.IgnoreQueryFilters().AsNoTracking().SingleAsync(certificate => certificate.Id == id);
            Assert.False(row.IsWildcard);
            Assert.Equal("example.com", row.Domain);
            Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), row.NotAfter);
        }
    }

    /// <summary>Asks the database whether the column exists, by its catalogue entry rather than by a query that would throw.</summary>
    /// <param name="dbContext">The Ssl context.</param>
    /// <returns>True when <c>ssl."Certificates"</c> has an <c>IsWildcard</c> column.</returns>
    private static async Task<bool> ColumnExistsAsync(SslDbContext dbContext)
    {
        // raw-sql: reads the catalogue for one column of a test database; both names are literals.
        return await dbContext.Database.SqlQuery<int>($"""
            SELECT 1 AS "Value" FROM information_schema.columns
            WHERE table_schema = 'ssl' AND table_name = 'Certificates' AND column_name = 'IsWildcard'
            """).AnyAsync();
    }

    /// <summary>Writes a certificate row in the previous release's schema.</summary>
    /// <param name="dbContext">The Ssl context.</param>
    /// <param name="id">The row's identity.</param>
    /// <returns>Resolves once the row is stored.</returns>
    private static async Task StoreCertificateAsync(SslDbContext dbContext, Guid id)
    {
        var notAfter = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var issuedAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        // raw-sql: writes a row in the previous release's schema, which no entity of this release can describe;
        // every value is a parameter and the statement runs only against a test database.
        await dbContext.Database.ExecuteSqlAsync($"""
            INSERT INTO ssl."Certificates"
                ("Id", "AccountId", "SiteId", "Domain", "Source", "NotAfter", "IssuedAt", "ConsecutiveRenewalFailures",
                 "LastRenewalErrorCode")
            VALUES
                ({id}, {Guid.NewGuid()}, {Guid.NewGuid()}, 'example.com', 'Acme', {notAfter}, {issuedAt}, 0, '')
            """);
    }

    /// <summary>Boots the host against this test's database.</summary>
    /// <returns>The host factory.</returns>
    private WebApplicationFactory<Program> CreateFactory()
    {
        return new ConfiguredHostFactory(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Security:EncryptionKey", Key);
            builder.UseSetting("Jwt:SigningKey", Key);

            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
        });
    }
}
