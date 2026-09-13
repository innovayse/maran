using Maran.Modules.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Identity.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="SecurityPolicy"/> onto the <c>identity.SecurityPolicy</c> table.
/// </summary>
public sealed class SecurityPolicyConfiguration : IEntityTypeConfiguration<SecurityPolicy>
{
    /// <summary>Configures the table, key, and column constraints for <see cref="SecurityPolicy"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<SecurityPolicy> builder)
    {
        // Singular, because the table holds one row by construction: the key is a constant nothing
        // generates a second value for, so "SecurityPolicies" would name a collection that cannot
        // exist.
        builder.ToTable("SecurityPolicy");

        builder.HasKey(policy => policy.Id);

        // Never generated. The key is SecurityPolicy.SingletonId and the value the entity sets must
        // reach the database: with a generated key the first save would silently write a different
        // id and every subsequent lookup by the constant would miss, which reads as "the policy did
        // not save" and is really "the policy saved somewhere nothing looks".
        builder.Property(policy => policy.Id).ValueGeneratedNever();

        builder.Property(policy => policy.MinimumPasswordLength).IsRequired();
        builder.Property(policy => policy.ForceTwoFactorForAdmins).IsRequired();
        builder.Property(policy => policy.MaxFailedLoginAttempts).IsRequired();
        builder.Property(policy => policy.LockoutMinutes).IsRequired();
        builder.Property(policy => policy.UpdatedAt).IsRequired();
    }
}
