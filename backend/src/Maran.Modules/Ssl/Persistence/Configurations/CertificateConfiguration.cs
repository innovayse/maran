using Maran.Modules.Ssl.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Ssl.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="Certificate"/> onto the <c>ssl.Certificates</c> table.</summary>
public sealed class CertificateConfiguration : IEntityTypeConfiguration<Certificate>
{
    /// <summary>Configures the table, keys, and column constraints for <see cref="Certificate"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<Certificate> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming: PascalCase everywhere") — never
        // the provider's lowercase default. Column names below match the property names exactly, so
        // they are left to EF Core's default rather than repeated via HasColumnName.
        builder.ToTable("Certificates");
        builder.HasKey(certificate => certificate.Id);

        builder.Property(certificate => certificate.AccountId)
            .IsRequired();

        builder.Property(certificate => certificate.SiteId)
            .IsRequired();

        builder.Property(certificate => certificate.Domain)
            .IsRequired()
            .HasMaxLength(253);

        builder.Property(certificate => certificate.Source)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(certificate => certificate.NotAfter)
            .IsRequired();

        builder.Property(certificate => certificate.IssuedAt)
            .IsRequired();

        builder.Property(certificate => certificate.LastRenewalAttemptAt);

        // An error CODE, so the column is short by design. Widening it is how an authority's own
        // sentence — and the material it quoted — would end up in this table (rules/security.md).
        builder.Property(certificate => certificate.LastRenewalErrorCode)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(certificate => certificate.ConsecutiveRenewalFailures)
            .IsRequired();

        // One certificate per domain across the whole server, because one vhost serves a domain and
        // one certificate is wired into that vhost. Enforced here as well as in the handlers: the
        // handler's check and the insert are not one atomic step, and two simultaneous issuances for
        // the same domain must not both write a row.
        builder.HasIndex(certificate => certificate.Domain)
            .IsUnique()
            .HasDatabaseName("IX_Certificates_Domain");

        // Every tenant-scoped read is "this account's certificates", which is the query the global
        // filter emits on every request from a customer.
        builder.HasIndex(certificate => certificate.AccountId)
            .HasDatabaseName("IX_Certificates_AccountId");

        // Renewal's own query: the whole server's certificates ordered by how soon they expire. Kept
        // as an index because it runs unfiltered over every account on the machine.
        builder.HasIndex(certificate => certificate.NotAfter)
            .HasDatabaseName("IX_Certificates_NotAfter");
    }
}
