using Maran.Modules.Sites.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Sites.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="PhpSetting"/> onto the <c>sites.PhpSettings</c> table.</summary>
public sealed class PhpSettingConfiguration : IEntityTypeConfiguration<PhpSetting>
{
    /// <summary>Configures the table, its key and the one-row-per-name-per-account index.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<PhpSetting> builder)
    {
        builder.ToTable("PhpSettings");
        builder.HasKey(setting => setting.Id);

        // The application assigns the id, so EF must not treat a Guid that arrives already set as the key
        // of a row that exists (the snapshot and the migration state the same).
        builder.Property(setting => setting.Id)
            .ValueGeneratedNever();

        builder.Property(setting => setting.AccountId)
            .IsRequired();

        builder.Property(setting => setting.Name)
            .IsRequired()
            .HasMaxLength(PhpSetting.TextMaxLength);

        builder.Property(setting => setting.Value)
            .IsRequired()
            .HasMaxLength(PhpSetting.TextMaxLength);

        // One row per setting per account. The unique index is also the read path of every pool write
        // (all of an account's settings), so no second index on AccountId is needed.
        builder.HasIndex(setting => new { setting.AccountId, setting.Name })
            .IsUnique()
            .HasDatabaseName("IX_PhpSettings_AccountId_Name");
    }
}
