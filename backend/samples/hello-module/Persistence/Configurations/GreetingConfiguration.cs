using Maran.Samples.Hello.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Samples.Hello.Persistence.Configurations;

/// <summary>Maps <see cref="Greeting"/> onto <c>hello.Greetings</c>.</summary>
public sealed class GreetingConfiguration : IEntityTypeConfiguration<Greeting>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Greeting> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming: PascalCase everywhere").
        builder.ToTable("Greetings");
        builder.HasKey(greeting => greeting.Id);

        builder.Property(greeting => greeting.Text)
            .IsRequired()
            .HasMaxLength(Greeting.TextMaxLength);
        builder.Property(greeting => greeting.CreatedAt).IsRequired();

        // The one read is "the newest greeting".
        builder.HasIndex(greeting => greeting.CreatedAt)
            .HasDatabaseName("IX_Greetings_CreatedAt");
    }
}
