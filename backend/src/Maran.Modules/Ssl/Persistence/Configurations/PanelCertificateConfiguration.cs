using Maran.Modules.Ssl.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Ssl.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="PanelCertificate"/> onto the <c>ssl.PanelCertificates</c> table.</summary>
public sealed class PanelCertificateConfiguration : IEntityTypeConfiguration<PanelCertificate>
{
    /// <summary>The longest host name DNS allows, which is the longest key this table holds.</summary>
    private const int HostNameMaxLength = 253;

    /// <summary>Configures the table, key and column constraints for <see cref="PanelCertificate"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<PanelCertificate> builder)
    {
        builder.ToTable("PanelCertificates");
        builder.HasKey(certificate => certificate.HostName);

        builder.Property(certificate => certificate.HostName)
            .IsRequired()
            .HasMaxLength(HostNameMaxLength);

        builder.Property(certificate => certificate.ExpiresAt)
            .IsRequired();

        builder.Property(certificate => certificate.IssuedAt)
            .IsRequired();
    }
}
