using Maran.Modules.Marketplace.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Marketplace.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="ModuleOperationLock"/> onto the <c>marketplace.ModuleOperationLocks</c>
/// table.
/// </summary>
/// <remarks>
/// The unique index on <c>ModuleId</c> is the lock itself: two callers inserting a claim for one module
/// cannot both succeed, whatever order they run in, because the database refuses the second insert.
/// </remarks>
public sealed class ModuleOperationLockConfiguration : IEntityTypeConfiguration<ModuleOperationLock>
{
    /// <summary>The longest module id a manifest may carry.</summary>
    private const int ModuleIdMaxLength = 64;

    /// <summary>The longest <c>MarketplaceOperation</c> name.</summary>
    private const int OperationMaxLength = 16;

    /// <summary>The longest semantic version text the panel parses.</summary>
    private const int VersionMaxLength = 128;

    /// <summary>Configures the table, key, columns and the unique index for <see cref="ModuleOperationLock"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<ModuleOperationLock> builder)
    {
        builder.ToTable("ModuleOperationLocks");

        builder.HasKey(claim => claim.Id);
        builder.Property(claim => claim.Id).ValueGeneratedNever();

        builder.Property(claim => claim.ModuleId)
            .IsRequired()
            .HasMaxLength(ModuleIdMaxLength);

        builder.Property(claim => claim.AcquiredAt)
            .IsRequired();

        // Nullable on purpose: a claim exists before its task does, and rows written before these columns
        // existed carry none (expand-only, rules/csharp.md "Schema changes are expand-then-contract").
        builder.Property(claim => claim.Operation)
            .HasConversion<string>()
            .HasMaxLength(OperationMaxLength);

        builder.Property(claim => claim.Version)
            .HasMaxLength(VersionMaxLength);

        builder.Property(claim => claim.TaskId);

        builder.HasIndex(claim => claim.ModuleId)
            .IsUnique()
            .HasDatabaseName("IX_ModuleOperationLocks_ModuleId");
    }
}
