using Maran.Modules.Accounts.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Accounts.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="Account"/> onto the <c>accounts.Accounts</c> table.</summary>
public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    /// <summary>Configures the table, keys, and column constraints for <see cref="Account"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming: PascalCase everywhere") —
        // never the provider's lowercase default. Column names below match the property names
        // exactly, so they are left to EF Core's default (already PascalCase) rather than
        // repeated via HasColumnName.
        builder.ToTable("Accounts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(a => a.PrimaryDomain)
            .IsRequired()
            .HasMaxLength(253);

        builder.Property(a => a.PlanId)
            .IsRequired();

        builder.Property(a => a.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(a => a.CreatedAt)
            .IsRequired();

        // The system user name (rules/architecture.md — one Linux user per account) must be
        // unique across the whole server, and a domain is claimed once, so both are unique here
        // rather than only validated in the command (defense in depth against races).
        builder.HasIndex(a => a.Name).IsUnique().HasDatabaseName("IX_Accounts_Name");
        builder.HasIndex(a => a.PrimaryDomain).IsUnique().HasDatabaseName("IX_Accounts_PrimaryDomain");

        // A shadow foreign key, not a navigation property: Account exposes only the scalar PlanId
        // (rules/csharp.md "Vertical slice shape" — no speculative cross-aggregate references), but
        // the relationship is still declared so the database enforces that PlanId always names a
        // real plan (defense in depth alongside CreateAccountCommandValidator's existence check).
        builder.HasOne<Plan>()
            .WithMany()
            .HasForeignKey(a => a.PlanId)
            .HasConstraintName("FK_Accounts_Plans_PlanId")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
