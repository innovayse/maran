using Maran.Modules.Mail.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Mail.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="DkimKey"/> onto the <c>mail.DkimKeys</c> table.</summary>
public sealed class DkimKeyConfiguration : IEntityTypeConfiguration<DkimKey>
{
    /// <summary>The longest selector the agent accepts, in characters.</summary>
    private const int SelectorMaxLength = 32;

    /// <summary>The longest base64 public key stored, in characters (a 4096-bit key is about 720).</summary>
    private const int PublicKeyMaxLength = 800;

    /// <summary>Configures the table, keys, indexes and column constraints for <see cref="DkimKey"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<DkimKey> builder)
    {
        builder.ToTable("DkimKeys");
        builder.HasKey(key => key.Id);
        builder.Property(key => key.Id).ValueGeneratedNever();

        builder.Property(key => key.MailDomainId).IsRequired();
        builder.Property(key => key.Selector).IsRequired().HasMaxLength(SelectorMaxLength);

        // The ciphertext of a PEM: unbounded text, never selected into a DTO.
        builder.Property(key => key.PrivateKeyEncrypted).IsRequired();
        builder.Property(key => key.PublicKey).IsRequired().HasMaxLength(PublicKeyMaxLength);
        builder.Property(key => key.State).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(key => key.CreatedAt).IsRequired();

        // A selector names one key of one domain: two keys under one name would publish one record for two.
        builder.HasIndex(key => new { key.MailDomainId, key.Selector })
            .IsUnique()
            .HasDatabaseName("IX_DkimKeys_MailDomainId_Selector");
    }
}
