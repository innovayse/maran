using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Policies;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Identity.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="DashboardPreference"/> onto the <c>identity.DashboardPreferences</c> table.</summary>
public sealed class DashboardPreferenceConfiguration : IEntityTypeConfiguration<DashboardPreference>
{
    /// <summary>Longest stored kind name; the enum is stored as its name.</summary>
    private const int KindLength = 16;

    /// <summary>Configures the table, keys, and column constraints for <see cref="DashboardPreference"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<DashboardPreference> builder)
    {
        builder.ToTable("DashboardPreferences");
        builder.HasKey(preference => preference.Id);

        builder.Property(preference => preference.UserId).IsRequired();

        builder.Property(preference => preference.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(KindLength);

        builder.Property(preference => preference.Key)
            .IsRequired()
            .HasMaxLength(DashboardLayoutPolicy.MaxKeyLength);

        builder.Property(preference => preference.Position).IsRequired();
        builder.Property(preference => preference.Hidden).IsRequired();

        // One entry per key per list per person; the index is also the access path of the person's
        // layout, and it settles two concurrent saves, so the loser is refused and not stored twice.
        builder.HasIndex(preference => new { preference.UserId, preference.Kind, preference.Key })
            .IsUnique()
            .HasDatabaseName("IX_DashboardPreferences_UserId_Kind_Key");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(preference => preference.UserId)
            .HasConstraintName("FK_DashboardPreferences_Users_UserId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
