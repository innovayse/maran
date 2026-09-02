using Maran.Modules.Databases.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Databases.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="Database"/> onto the <c>databases.Databases</c> table.</summary>
/// <remarks>
/// Read the column list for what is NOT here: there is no password column of any kind. That absence
/// is the design (see <see cref="Database"/>), and it is asserted by a test rather than left to a
/// reviewer noticing a new property one day.
/// </remarks>
public sealed class DatabaseConfiguration : IEntityTypeConfiguration<Database>
{
    /// <summary>MySQL's identifier ceiling, which a fully-qualified database name cannot exceed.</summary>
    private const int MySqlIdentifierMaxLength = 64;

    /// <summary>MySQL's user-name ceiling, which a fully-qualified user name cannot exceed.</summary>
    private const int MySqlUserNameMaxLength = 32;

    /// <summary>Configures the table, keys, and column constraints for <see cref="Database"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<Database> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming: PascalCase everywhere") —
        // never the provider's lowercase default.
        builder.ToTable("Databases");
        builder.HasKey(database => database.Id);

        builder.Property(database => database.AccountId)
            .IsRequired();

        builder.Property(database => database.Name)
            .IsRequired()
            .HasMaxLength(MySqlIdentifierMaxLength);

        builder.Property(database => database.FullName)
            .IsRequired()
            .HasMaxLength(MySqlIdentifierMaxLength);

        builder.Property(database => database.DbUserName)
            .IsRequired()
            .HasMaxLength(MySqlUserNameMaxLength);

        builder.Property(database => database.DbUserNameSuffix)
            .IsRequired()
            .HasMaxLength(MySqlUserNameMaxLength);

        builder.Property(database => database.CreatedAt)
            .IsRequired();

        // The name the CUSTOMER chose is unique within their account and deliberately not across
        // the server: the account prefix is what lets two customers both have a `shop`. Scoping it
        // to the whole server instead would hand the first tenant to ask a name every other tenant
        // then cannot use, which is the problem the prefix exists to solve.
        builder.HasIndex(database => new { database.AccountId, database.Name })
            .IsUnique()
            .HasDatabaseName("IX_Databases_AccountId_Name");

        // MySQL's own namespaces ARE server-wide, so the prefixed names are unique across every
        // account. Enforced here as well as by the pre-insert check, because that check and the
        // insert are not one atomic step: two simultaneous creations of the same name must not both
        // produce a row, and the loser must arrive as a typed conflict rather than as two rows
        // claiming one database.
        builder.HasIndex(database => database.FullName)
            .IsUnique()
            .HasDatabaseName("IX_Databases_FullName");

        // A MySQL user is one login. Two of the account's databases sharing one would mean a reset
        // for either silently re-credentials both, and a drop of either removes the other's login.
        builder.HasIndex(database => database.DbUserName)
            .IsUnique()
            .HasDatabaseName("IX_Databases_DbUserName");

        // Every tenant-scoped read is "this account's databases", which is the query the global
        // filter emits on every single request from a customer.
        builder.HasIndex(database => database.AccountId).HasDatabaseName("IX_Databases_AccountId");
    }
}
