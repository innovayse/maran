using Maran.Modules.Databases.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Databases.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="DatabaseGrant"/> onto the <c>databases.DatabaseGrants</c> table.</summary>
public sealed class DatabaseGrantConfiguration : IEntityTypeConfiguration<DatabaseGrant>
{
    /// <summary>Configures the table, keys, and the cascade from the database.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<DatabaseGrant> builder)
    {
        builder.ToTable("DatabaseGrants");
        builder.HasKey(grant => grant.Id);

        // The identity is assigned by the aggregate when the grant is created. EF Core would otherwise
        // treat a grant added to an already-tracked user's list, whose key is set, as an existing row
        // and send an UPDATE for it.
        builder.Property(grant => grant.Id).ValueGeneratedNever();

        builder.Property(grant => grant.DatabaseUserId).IsRequired();
        builder.Property(grant => grant.DatabaseId).IsRequired();
        builder.Property(grant => grant.Privileges).IsRequired();
        builder.Property(grant => grant.CreatedAt).IsRequired();

        // Cascade: a grant on a database exists for exactly as long as the database does, which is what
        // keeps a dropped database from leaving a privilege behind for a later one of the same name.
        builder.HasOne<Database>()
            .WithMany()
            .HasForeignKey(grant => grant.DatabaseId)
            .OnDelete(DeleteBehavior.Cascade);

        // One row per user and database; holding nothing is having no row.
        builder.HasIndex(grant => new { grant.DatabaseUserId, grant.DatabaseId })
            .IsUnique()
            .HasDatabaseName("IX_DatabaseGrants_DatabaseUserId_DatabaseId");

        // "Which grants does this database carry" is the read the drop path and the screens make.
        builder.HasIndex(grant => grant.DatabaseId).HasDatabaseName("IX_DatabaseGrants_DatabaseId");
    }
}
