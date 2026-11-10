using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Policies;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Dns.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="ZoneTemplate"/> onto the <c>dns.ZoneTemplates</c> table.</summary>
public sealed class ZoneTemplateConfiguration : IEntityTypeConfiguration<ZoneTemplate>
{
    /// <summary>Configures the table, the columns and the unique name for <see cref="ZoneTemplate"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<ZoneTemplate> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming: PascalCase everywhere").
        builder.ToTable("ZoneTemplates");

        builder.HasKey(template => template.Id);
        builder.Property(template => template.Id).ValueGeneratedNever();

        builder.Property(template => template.Name).IsRequired().HasMaxLength(TemplateNamePolicy.MaxNameLength);
        builder.Property(template => template.NameKey).IsRequired().HasMaxLength(TemplateNamePolicy.MaxNameLength);
        builder.Property(template => template.Description)
            .IsRequired()
            .HasMaxLength(TemplateNamePolicy.MaxDescriptionLength);
        builder.Property(template => template.IsBuiltIn).IsRequired();
        builder.Property(template => template.CreatedAt).IsRequired();
        builder.Property(template => template.UpdatedAt).IsRequired();

        // The domain refuses a taken name first with a typed answer; this index is the guarantee under it.
        builder.HasIndex(template => template.NameKey)
            .IsUnique()
            .HasDatabaseName("IX_ZoneTemplates_NameKey");
    }
}
