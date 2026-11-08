using Maran.Modules.Mail.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Mail.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="MailAliasTarget"/> onto the <c>mail.MailAliasTargets</c> table.</summary>
public sealed class MailAliasTargetConfiguration : IEntityTypeConfiguration<MailAliasTarget>
{
    /// <summary>The longest address stored, in characters (RFC 5321 forward-path).</summary>
    private const int AddressMaxLength = 320;

    /// <summary>Configures the table, keys, indexes and column constraints for <see cref="MailAliasTarget"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<MailAliasTarget> builder)
    {
        builder.ToTable("MailAliasTargets");
        builder.HasKey(target => target.Id);
        builder.Property(target => target.Id).ValueGeneratedNever();

        builder.Property(target => target.MailAliasId).IsRequired();
        builder.Property(target => target.Address).IsRequired().HasMaxLength(AddressMaxLength);
        builder.Property(target => target.Position).IsRequired();

        builder.HasIndex(target => new { target.MailAliasId, target.Position })
            .IsUnique()
            .HasDatabaseName("IX_MailAliasTargets_MailAliasId_Position");
    }
}
