using Maran.Modules.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Identity.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="AuditRetentionPolicy"/> onto the <c>identity.AuditRetentionPolicies</c> table.
/// </summary>
public sealed class AuditRetentionPolicyConfiguration : IEntityTypeConfiguration<AuditRetentionPolicy>
{
    /// <summary>Configures the table, key, and column constraints for <see cref="AuditRetentionPolicy"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<AuditRetentionPolicy> builder)
    {
        builder.ToTable("AuditRetentionPolicies");

        builder.HasKey(policy => policy.Id);

        // Never generated: the key is AuditRetentionPolicy.SingletonId, and a generated one would make
        // every lookup by the constant miss the row the first save wrote.
        builder.Property(policy => policy.Id).ValueGeneratedNever();

        // Nullable on purpose: null is "keep indefinitely", which is the default, not a missing value.
        builder.Property(policy => policy.RetentionDays);
        builder.Property(policy => policy.UpdatedAt).IsRequired();
        builder.Property(policy => policy.LastRunAt);
    }
}
