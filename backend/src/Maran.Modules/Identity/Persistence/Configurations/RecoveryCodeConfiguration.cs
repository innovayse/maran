using Maran.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Identity.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="RecoveryCode"/> onto the <c>identity.RecoveryCodes</c> table.</summary>
public sealed class RecoveryCodeConfiguration : IEntityTypeConfiguration<RecoveryCode>
{
    /// <summary>Configures the table, keys, and column constraints for <see cref="RecoveryCode"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<RecoveryCode> builder)
    {
        builder.ToTable("RecoveryCodes");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.UserId)
            .IsRequired();

        builder.Property(c => c.CodeHash)
            .IsRequired()
            .HasMaxLength(256);

        // Verifying a recovery code walks the user's unspent codes, so this is the access path.
        builder.HasIndex(c => c.UserId).HasDatabaseName("IX_RecoveryCodes_UserId");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .HasConstraintName("FK_RecoveryCodes_Users_UserId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
