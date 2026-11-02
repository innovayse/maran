using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Policies;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Identity.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="PinnedPage"/> onto the <c>identity.PinnedPages</c> table.</summary>
public sealed class PinnedPageConfiguration : IEntityTypeConfiguration<PinnedPage>
{
    /// <summary>Configures the table, keys, and column constraints for <see cref="PinnedPage"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<PinnedPage> builder)
    {
        builder.ToTable("PinnedPages");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.UserId)
            .IsRequired();

        builder.Property(p => p.ItemKey)
            .IsRequired()
            .HasMaxLength(PinnedPagePolicy.MaxItemKeyLength);

        builder.Property(p => p.Position)
            .IsRequired();

        builder.Property(p => p.PinnedAt)
            .IsRequired();

        // One pin per page per user; the index is also the access path of the user's list. It is
        // what settles two concurrent pins of the same page, so the loser is refused and not stored twice.
        builder.HasIndex(p => new { p.UserId, p.ItemKey })
            .IsUnique()
            .HasDatabaseName("IX_PinnedPages_UserId_ItemKey");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .HasConstraintName("FK_PinnedPages_Users_UserId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
