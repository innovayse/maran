using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Policies;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Mail.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="MailAlias"/> onto the <c>mail.MailAliases</c> table.</summary>
public sealed class MailAliasConfiguration : IEntityTypeConfiguration<MailAlias>
{
    /// <summary>Configures the table, keys, indexes and column constraints for <see cref="MailAlias"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<MailAlias> builder)
    {
        builder.ToTable("MailAliases");
        builder.HasKey(alias => alias.Id);
        builder.Property(alias => alias.Id).ValueGeneratedNever();

        builder.Property(alias => alias.AccountId).IsRequired();
        builder.Property(alias => alias.MailDomainId).IsRequired();
        builder.Property(alias => alias.LocalPart).IsRequired().HasMaxLength(MailDefaults.LocalPartMaxLength);
        builder.Property(alias => alias.Kind).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(alias => alias.CreatedAt).IsRequired();

        // One address, one alias; and because the catch-all is stored under a local part no mailbox can
        // have, at most one catch-all per domain.
        builder.HasIndex(alias => new { alias.MailDomainId, alias.LocalPart })
            .IsUnique()
            .HasDatabaseName("IX_MailAliases_MailDomainId_LocalPart");
        builder.HasIndex(alias => alias.AccountId).HasDatabaseName("IX_MailAliases_AccountId");

        builder.HasOne(alias => alias.MailDomain)
            .WithMany()
            .HasForeignKey(alias => alias.MailDomainId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(alias => alias.Targets)
            .WithOne(target => target.MailAlias)
            .HasForeignKey(target => target.MailAliasId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(alias => alias.Targets).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
