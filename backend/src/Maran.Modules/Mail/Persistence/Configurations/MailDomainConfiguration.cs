using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Policies;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Mail.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="MailDomain"/> onto the <c>mail.MailDomains</c> table.</summary>
public sealed class MailDomainConfiguration : IEntityTypeConfiguration<MailDomain>
{
    /// <summary>Configures the table, keys, indexes and column constraints for <see cref="MailDomain"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<MailDomain> builder)
    {
        builder.ToTable("MailDomains");
        builder.HasKey(domain => domain.Id);
        builder.Property(domain => domain.Id).ValueGeneratedNever();

        builder.Property(domain => domain.AccountId).IsRequired();
        builder.Property(domain => domain.Name).IsRequired().HasMaxLength(MailDefaults.DomainMaxLength);
        builder.Property(domain => domain.SiteId).IsRequired();
        builder.Property(domain => domain.CreatedAt).IsRequired();

        // The server accepts mail for a domain on behalf of exactly one account, so the name is unique
        // across every account and not merely within one. The handler checks first to answer with a typed
        // conflict; this index is what makes two simultaneous creations unable to both succeed.
        builder.HasIndex(domain => domain.Name).IsUnique().HasDatabaseName("IX_MailDomains_Name");
        builder.HasIndex(domain => domain.AccountId).HasDatabaseName("IX_MailDomains_AccountId");

        builder.HasMany(domain => domain.DkimKeys)
            .WithOne(key => key.MailDomain)
            .HasForeignKey(key => key.MailDomainId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(domain => domain.DkimKeys).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
