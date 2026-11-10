using Maran.Modules.Marketplace.Domain.Entities;
using Maran.Modules.Marketplace.Domain.Enums;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Marketplace.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="InstalledPackage"/> onto the <c>marketplace.InstalledPackages</c> table.
/// </summary>
/// <remarks>
/// Read the column list for what is NOT here: no <c>AccountId</c>, because the marketplace belongs to the
/// installation, and no lease, grant, URL or error text, because a row that never holds one cannot leak one
/// (rules/security.md item 8). The state is stored as text so a reader of the table in <c>psql</c> sees a
/// word, and a reordered enum cannot silently renumber history.
/// </remarks>
public sealed class InstalledPackageConfiguration : IEntityTypeConfiguration<InstalledPackage>
{
    /// <summary>The longest module id a manifest may carry.</summary>
    private const int ModuleIdMaxLength = 64;

    /// <summary>The longest semantic version text the panel parses.</summary>
    private const int VersionMaxLength = 128;

    /// <summary>The length of a SHA-256 digest written as hexadecimal.</summary>
    private const int Sha256HexLength = 64;

    /// <summary>The longest <see cref="InstalledPackageState"/> name.</summary>
    private const int StateMaxLength = 16;

    /// <summary>Configures the table, key, columns and indexes for <see cref="InstalledPackage"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<InstalledPackage> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming: PascalCase everywhere").
        builder.ToTable("InstalledPackages");

        builder.HasKey(package => package.Id);
        builder.Property(package => package.Id).ValueGeneratedNever();

        builder.Property(package => package.ModuleId)
            .IsRequired()
            .HasMaxLength(ModuleIdMaxLength);

        builder.Property(package => package.Version)
            .IsRequired()
            .HasMaxLength(VersionMaxLength);

        builder.Property(package => package.PackageSha256)
            .IsRequired()
            .HasMaxLength(Sha256HexLength);

        builder.Property(package => package.InstalledAt)
            .IsRequired();

        builder.Property(package => package.InstalledBy)
            .IsRequired();

        builder.Property(package => package.PreviousVersion)
            .HasMaxLength(VersionMaxLength);

        builder.Property(package => package.State)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(StateMaxLength);

        builder.Property(package => package.StateChangedAt)
            .IsRequired();

        // Nullable: rows written before the name was kept have none (expand-only).
        builder.Property(package => package.DisplayName)
            .HasMaxLength(InstalledPackage.DisplayNameMaxLength);

        // The history of one module is read by its id, newest first.
        builder.HasIndex(package => new { package.ModuleId, package.InstalledAt })
            .HasDatabaseName("IX_InstalledPackages_ModuleId_InstalledAt");
    }
}
