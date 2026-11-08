using Maran.Modules.Backups.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Backups.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="AccountBackupOptOut"/> onto the <c>backups.AccountBackupOptOuts</c> table.
/// </summary>
public sealed class AccountBackupOptOutConfiguration : IEntityTypeConfiguration<AccountBackupOptOut>
{
    /// <summary>Configures the table and key for <see cref="AccountBackupOptOut"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<AccountBackupOptOut> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming"), never the provider's default.
        builder.ToTable("AccountBackupOptOuts");

        // The account id IS the key: one row per opted-out account, and the primary key is what makes a
        // repeated delivery of the creation event a collision instead of a second row. No foreign key —
        // the account row lives in the Accounts schema, which this module never reads
        // (rules/architecture.md); the deletion handler removes this row with the account.
        builder.HasKey(row => row.AccountId);

        builder.Property(row => row.AccountId)
            .ValueGeneratedNever();

        builder.Property(row => row.OptedOutAt)
            .IsRequired();
    }
}
