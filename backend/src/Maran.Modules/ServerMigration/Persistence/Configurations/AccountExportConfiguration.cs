using Maran.Modules.ServerMigration.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.ServerMigration.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="AccountExport"/> onto <c>servermigration.AccountExports</c>.</summary>
/// <remarks>
/// A grant belongs to exactly one export and goes with it. The export names the account as
/// <c>SourceAccountId</c> and carries no tenant filter: it is the administrator's bookkeeping.
/// </remarks>
public sealed class AccountExportConfiguration : IEntityTypeConfiguration<AccountExport>
{
    /// <summary>Configures the table, columns and the index the list sorts on.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<AccountExport> builder)
    {
        builder.ToTable("AccountExports");
        builder.HasKey(export => export.Id);
        builder.Property(export => export.Id).ValueGeneratedNever();
        builder.Property(export => export.SourceAccountId).IsRequired();
        builder.Property(export => export.SourceAccountName).IsRequired().HasMaxLength(AccountExport.AccountNameMaxLength);
        builder.Property(export => export.BackupId).IsRequired();
        builder.Property(export => export.SizeBytes).IsRequired();
        builder.Property(export => export.Sha256).IsRequired().HasMaxLength(ExportGrant.DigestLength);
        builder.Property(export => export.DatabaseCount).IsRequired();
        builder.Property(export => export.SectionsJson).IsRequired();
        builder.Property(export => export.CreatedByUserId).IsRequired();
        builder.Property(export => export.CreatedAt).IsRequired();
        builder.Property(export => export.ExpiresAt).IsRequired();

        builder.HasIndex(export => export.CreatedAt).HasDatabaseName("IX_AccountExports_CreatedAt");
    }
}
