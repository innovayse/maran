using Maran.Modules.Databases.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Databases.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="DatabaseUser"/> onto the <c>databases.DatabaseUsers</c> table.</summary>
/// <remarks>As with <see cref="DatabaseConfiguration"/>, the absence of any password column is the design.</remarks>
public sealed class DatabaseUserConfiguration : IEntityTypeConfiguration<DatabaseUser>
{
    /// <summary>MySQL's user-name ceiling, which a fully-qualified user name cannot exceed.</summary>
    private const int MySqlUserNameMaxLength = 32;

    /// <summary>Configures the table, keys, and column constraints for <see cref="DatabaseUser"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<DatabaseUser> builder)
    {
        builder.ToTable("DatabaseUsers");
        builder.HasKey(user => user.Id);

        builder.Property(user => user.AccountId).IsRequired();
        builder.Property(user => user.NameSuffix).IsRequired().HasMaxLength(MySqlUserNameMaxLength);
        builder.Property(user => user.FullName).IsRequired().HasMaxLength(MySqlUserNameMaxLength);
        builder.Property(user => user.CreatedAt).IsRequired();

        // The grants are reached through the aggregate's private list, never through a settable property.
        builder.HasMany(user => user.Grants)
            .WithOne(grant => grant.DatabaseUser)
            .HasForeignKey(grant => grant.DatabaseUserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(user => user.Grants).UsePropertyAccessMode(PropertyAccessMode.Field);

        // The customer's own namespace: one login name once per account.
        builder.HasIndex(user => new { user.AccountId, user.NameSuffix })
            .IsUnique()
            .HasDatabaseName("IX_DatabaseUsers_AccountId_NameSuffix");

        // MySQL's namespace for logins is server-wide, so the prefixed names are unique across accounts;
        // enforced here because the pre-insert check and the insert are not one atomic step.
        builder.HasIndex(user => user.FullName)
            .IsUnique()
            .HasDatabaseName("IX_DatabaseUsers_FullName");
    }
}
