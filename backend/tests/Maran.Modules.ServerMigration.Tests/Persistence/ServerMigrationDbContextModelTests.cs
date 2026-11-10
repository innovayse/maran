using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Maran.Modules.ServerMigration.Tests.Persistence;

/// <summary>
/// What the registry's model promises the database. The unique indexes are PostgreSQL facts that only a real
/// database enforces, so they are read from the model that generates the migration; the key's encryption is proved
/// by writing through the in-memory provider, which applies the same converter.
/// </summary>
public sealed class ServerMigrationDbContextModelTests
{
    /// <summary>Every entity is in the <c>servermigration</c> schema and a PascalCase table.</summary>
    [Fact]
    public void Every_entity_is_mapped_to_the_migration_schema_in_pascal_case()
    {
        using var context = InMemoryMigrationDatabase.Open(Guid.NewGuid().ToString());

        var tables = context.Model.GetEntityTypes()
            .Select(entity => { return entity.GetSchema() + "." + entity.GetTableName(); })
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                "servermigration.AccountExports",
                "servermigration.AccountImports",
                "servermigration.DiscoveredAccounts",
                "servermigration.DiscoveredDatabases",
                "servermigration.DiscoveredSites",
                "servermigration.DiscoveryWarnings",
                "servermigration.ExportGrants",
                "servermigration.MigrationJobAccounts",
                "servermigration.MigrationJobs",
                "servermigration.MigrationSigningKeys",
                "servermigration.RemoteServerHostKeyCandidates",
                "servermigration.RemoteServers",
            ],
            tables);
    }

    /// <summary>The label is unique, and so is the combination of host, port and login.</summary>
    [Fact]
    public void A_name_and_an_endpoint_are_each_registered_once()
    {
        var entity = Entity<RemoteServer>();

        var name = entity.GetIndexes().Single(index => { return index.Properties.Select(p => { return p.Name; }).SequenceEqual(new[] { nameof(RemoteServer.Name) }); });
        var endpoint = entity.GetIndexes().Single(index =>
        {
            return index.Properties.Select(p => { return p.Name; })
                .SequenceEqual(new[] { nameof(RemoteServer.Host), nameof(RemoteServer.Port), nameof(RemoteServer.SshUser) });
        });

        Assert.True(name.IsUnique);
        Assert.True(endpoint.IsUnique);
        Assert.Equal("IX_RemoteServers_Name", name.GetDatabaseName());
        Assert.Equal("IX_RemoteServers_Endpoint", endpoint.GetDatabaseName());
    }

    /// <summary>
    /// Removing a server removes everything discovered on it, in the database and not by a handler's memory; a
    /// migration job is the one exception and holds the server back (<see cref="DeleteBehavior.Restrict"/>), because
    /// removing a server a job still uses is refused with <c>RemoteServerInUse</c>.
    /// </summary>
    [Fact]
    public void Everything_hanging_off_a_server_is_deleted_with_it()
    {
        using var context = InMemoryMigrationDatabase.Open(Guid.NewGuid().ToString());
        var servers = context.Model.FindEntityType(typeof(RemoteServer))!;

        var keys = context.Model.GetEntityTypes()
            .SelectMany(entity => { return entity.GetForeignKeys(); })
            .Where(key => { return key.PrincipalEntityType == servers || key.PrincipalEntityType.ClrType == typeof(DiscoveredAccount); })
            .ToList();
        var jobKey = Assert.Single(keys, key => { return key.DeclaringEntityType.ClrType == typeof(MigrationJob); });
        var cascades = keys.Where(key => { return key != jobKey; }).ToList();

        Assert.Equal(DeleteBehavior.Restrict, jobKey.DeleteBehavior);
        Assert.Equal(5, cascades.Count);
        Assert.All(cascades, key => { Assert.Equal(DeleteBehavior.Cascade, key.DeleteBehavior); Assert.True(key.IsRequired); });
    }

    /// <summary>The module's rows are the installation's: no account id, no reseller id, no query filter.</summary>
    [Fact]
    public void No_entity_carries_a_tenant_column_or_a_query_filter()
    {
        using var context = InMemoryMigrationDatabase.Open(Guid.NewGuid().ToString());

        foreach (var entity in context.Model.GetEntityTypes())
        {
            Assert.Null(entity.FindProperty("AccountId"));
            Assert.Null(entity.FindProperty("ResellerId"));
            Assert.Null(entity.GetQueryFilter());
        }
    }

    /// <summary>The private key column goes through the cipher and the other key columns do not.</summary>
    [Fact]
    public void Only_the_private_key_column_has_a_converter()
    {
        var entity = Entity<RemoteServer>();

        Assert.NotNull(entity.FindProperty(nameof(RemoteServer.PrivateKeyCipher))!.GetValueConverter());
        Assert.Null(entity.FindProperty(nameof(RemoteServer.PublicKey))!.GetValueConverter());
        Assert.Null(entity.FindProperty(nameof(RemoteServer.HostKey))!.GetValueConverter());
    }

    /// <summary>Written through the context the key reaches the store as ciphertext and reads back as the key.</summary>
    [Fact]
    public async Task The_private_key_is_stored_as_ciphertext_and_read_back_as_the_key()
    {
        var name = Guid.NewGuid().ToString();
        var server = RemoteServerTestValues.Registered();
        await using (var write = InMemoryMigrationDatabase.Open(name))
        {
            write.RemoteServers.Add(server);
            await write.SaveChangesAsync();
        }

        await using var read = InMemoryMigrationDatabase.Open(name);
        var loaded = await read.RemoteServers.AsNoTracking().SingleAsync();
        Assert.Equal(RemoteServerTestValues.PrivateKeySentinel, loaded.PrivateKeyCipher);

        // The raw stored value, read with the converter bypassed: the store holds the cipher's output only.
        var property = read.Model.FindEntityType(typeof(RemoteServer))!.FindProperty(nameof(RemoteServer.PrivateKeyCipher))!;
        var converter = property.GetValueConverter()!;
        var stored = converter.ConvertToProvider(RemoteServerTestValues.PrivateKeySentinel);
        Assert.Equal(PrefixEncryptionService.Prefix + RemoteServerTestValues.PrivateKeySentinel, stored);
        Assert.NotEqual(RemoteServerTestValues.PrivateKeySentinel, stored);
    }

    /// <summary>A host key candidate set round-trips through the backing field.</summary>
    [Fact]
    public async Task Host_key_candidates_round_trip_through_the_server()
    {
        var name = Guid.NewGuid().ToString();
        await using (var write = InMemoryMigrationDatabase.Open(name))
        {
            write.RemoteServers.Add(RemoteServerTestValues.Scanned());
            await write.SaveChangesAsync();
        }

        await using var read = InMemoryMigrationDatabase.Open(name);
        var loaded = await read.RemoteServers.Include(server => server.HostKeyCandidates).SingleAsync();

        Assert.Equal(2, loaded.HostKeyCandidates.Count);
    }

    /// <summary>The mapped entity type of one CLR type.</summary>
    /// <typeparam name="T">The entity.</typeparam>
    /// <returns>The entity type.</returns>
    private static IEntityType Entity<T>()
    {
        using var context = InMemoryMigrationDatabase.Open(Guid.NewGuid().ToString());
        var entity = context.Model.FindEntityType(typeof(T));
        Assert.NotNull(entity);
        return entity;
    }
}
