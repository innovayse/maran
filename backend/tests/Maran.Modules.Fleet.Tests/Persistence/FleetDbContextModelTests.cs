using Maran.Modules.Fleet.Domain.Entities;
using Maran.Modules.Fleet.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Maran.Modules.Fleet.Tests.Persistence;

/// <summary>
/// What the registry's model promises the database. The unique indexes are PostgreSQL facts that only a
/// real database enforces, so they are read from the model that generates the migration; the Host
/// integration tests prove the database holds them.
/// </summary>
public sealed class FleetDbContextModelTests
{
    /// <summary>The fields <see cref="Server"/> documents as stored, in ordinal order.</summary>
    private static readonly string[] DocumentedColumns =
    [
        nameof(Server.Address),
        nameof(Server.ApprovedAt),
        nameof(Server.CreatedAt),
        nameof(Server.Fingerprint),
        nameof(Server.Id),
        nameof(Server.Kind),
        nameof(Server.Name),
        nameof(Server.Port),
        nameof(Server.State),
    ];

    /// <summary>The server is mapped to the <c>Servers</c> table of the <c>fleet</c> schema, in PascalCase.</summary>
    [Fact]
    public void Servers_are_mapped_to_the_fleet_schema_in_pascal_case()
    {
        var entity = ServerEntity();

        Assert.Equal("fleet", entity.GetSchema());
        Assert.Equal("Servers", entity.GetTableName());
    }

    /// <summary>The identity is chosen by the panel, never generated, so the local server keeps its well-known id.</summary>
    [Fact]
    public void The_identity_is_never_generated_by_the_database()
    {
        var key = ServerEntity().FindPrimaryKey();

        Assert.NotNull(key);
        var id = Assert.Single(key.Properties);
        Assert.Equal(nameof(Server.Id), id.Name);
        Assert.Equal(ValueGenerated.Never, id.ValueGenerated);
    }

    /// <summary>Two servers cannot share a name, so the name an administrator picks one by is unambiguous.</summary>
    [Fact]
    public void A_server_name_is_unique()
    {
        var index = IndexOn(nameof(Server.Name));

        Assert.True(index.IsUnique);
        Assert.Null(index.GetFilter());
        Assert.Equal("IX_Servers_Name", index.GetDatabaseName());
    }

    /// <summary>
    /// Two rows cannot pin one certificate, or one host would stand as two nodes; the index is partial because the
    /// local server and a node that has not enrolled yet have no fingerprint, and any number of rows may lack one.
    /// </summary>
    [Fact]
    public void A_fingerprint_is_unique_among_the_rows_that_have_one()
    {
        var index = IndexOn(nameof(Server.Fingerprint));

        Assert.True(index.IsUnique);
        Assert.Equal("\"Fingerprint\" IS NOT NULL", index.GetFilter());
        Assert.Equal("IX_Servers_Fingerprint", index.GetDatabaseName());
    }

    /// <summary>The column sizes are the entity's limits, so the database cannot hold what the entity would refuse.</summary>
    [Fact]
    public void The_column_sizes_are_the_entity_limits()
    {
        var entity = ServerEntity();

        Assert.Equal(Server.NameMaxLength, entity.FindProperty(nameof(Server.Name))!.GetMaxLength());
        Assert.Equal(Server.AddressMaxLength, entity.FindProperty(nameof(Server.Address))!.GetMaxLength());
        Assert.Equal(64, entity.FindProperty(nameof(Server.Fingerprint))!.GetMaxLength());
    }

    /// <summary>Only what is absent for a kind of server is nullable, so a missing name or state cannot be stored.</summary>
    [Fact]
    public void Only_the_fields_a_kind_of_server_may_lack_are_nullable()
    {
        var entity = ServerEntity();

        Assert.False(entity.FindProperty(nameof(Server.Name))!.IsNullable);
        Assert.False(entity.FindProperty(nameof(Server.Kind))!.IsNullable);
        Assert.False(entity.FindProperty(nameof(Server.State))!.IsNullable);
        Assert.False(entity.FindProperty(nameof(Server.CreatedAt))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(Server.Address))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(Server.Port))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(Server.Fingerprint))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(Server.ApprovedAt))!.IsNullable);
    }

    /// <summary>
    /// The registry is the installation's, not a tenant's: it carries no account id and no query filter, which
    /// is what the tenant-scope census expects of an administrator-only table.
    /// </summary>
    [Fact]
    public void The_registry_has_no_account_id_and_no_tenant_filter()
    {
        var entity = ServerEntity();

        Assert.Null(entity.FindProperty("AccountId"));
        Assert.Null(entity.GetQueryFilter());
    }

    /// <summary>The persisted columns are exactly the fields the entity documents, so nothing observed is stored.</summary>
    [Fact]
    public void The_persisted_columns_are_exactly_the_documented_fields()
    {
        var columns = ServerEntity().GetProperties().Select(property =>
        {
            return property.Name;
        }).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(DocumentedColumns, columns);
    }

    /// <summary>The model maps the one entity and nothing else, and a lookup for a type it does not map comes back empty.</summary>
    [Fact]
    public void The_model_maps_exactly_one_entity_and_a_lookup_can_come_back_empty()
    {
        using var context = InMemoryFleetDatabase.Open(nameof(The_model_maps_exactly_one_entity_and_a_lookup_can_come_back_empty));

        Assert.Null(context.Model.FindEntityType(typeof(string)));
        Assert.Single(context.Model.GetEntityTypes());
    }

    /// <summary>The mapped server entity type of a fresh context.</summary>
    /// <returns>The entity type the model holds for <see cref="Server"/>.</returns>
    private static IEntityType ServerEntity()
    {
        using var context = InMemoryFleetDatabase.Open(Guid.NewGuid().ToString());
        var entity = context.Model.FindEntityType(typeof(Server));
        Assert.NotNull(entity);
        return entity;
    }

    /// <summary>The single-column index over a property.</summary>
    /// <param name="property">The property's name.</param>
    /// <returns>The index; the test fails when there is none.</returns>
    private static IIndex IndexOn(string property)
    {
        var index = ServerEntity().GetIndexes().SingleOrDefault(candidate =>
        {
            return candidate.Properties.Count == 1 && candidate.Properties[0].Name == property;
        });
        Assert.NotNull(index);
        return index;
    }
}
