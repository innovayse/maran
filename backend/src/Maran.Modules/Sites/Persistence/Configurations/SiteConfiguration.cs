using Maran.Modules.Sites.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Sites.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="Site"/> onto the <c>sites.Sites</c> table.</summary>
public sealed class SiteConfiguration : IEntityTypeConfiguration<Site>
{
    /// <summary>Configures the table, keys, and column constraints for <see cref="Site"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<Site> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming: PascalCase everywhere") —
        // never the provider's lowercase default. Column names below match the property names
        // exactly, so they are left to EF Core's default rather than repeated via HasColumnName.
        builder.ToTable("Sites");
        builder.HasKey(site => site.Id);

        builder.Property(site => site.AccountId)
            .IsRequired();

        builder.Property(site => site.Domain)
            .IsRequired()
            .HasMaxLength(253);

        // A primitive collection, not a child table: an alias has no identity, no lifecycle and is
        // never queried on its own — it is one of the facts handed to the renderer whole.
        builder.PrimitiveCollection(site => site.Aliases)
            .IsRequired();

        builder.Property(site => site.BackendType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(site => site.PhpVersion)
            .IsRequired()
            .HasMaxLength(8);

        builder.Property(site => site.ProxyUpstream)
            .IsRequired()
            .HasMaxLength(253);

        builder.Property(site => site.DocumentRoot)
            .IsRequired()
            .HasMaxLength(4096);

        builder.Property(site => site.HasCertificate)
            .IsRequired();

        builder.Property(site => site.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(site => site.CreatedAt)
            .IsRequired();

        // A domain is served by exactly one vhost on this server, so the claim is unique across
        // every account rather than within one. Enforced here as well as in the create handler:
        // the handler's check and the insert are not one atomic step, and two simultaneous
        // requests for the same domain must not both succeed.
        builder.HasIndex(site => site.Domain).IsUnique().HasDatabaseName("IX_Sites_Domain");

        // Every tenant-scoped read is "this account's sites", which is the query the global filter
        // emits on every single request from a customer.
        builder.HasIndex(site => site.AccountId).HasDatabaseName("IX_Sites_AccountId");
    }
}
