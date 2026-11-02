using Maran.Modules.Monitoring.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Monitoring.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="LogLevelSetting"/> onto <c>monitoring.LogLevelSettings</c>, one row
/// of which is the whole table.
/// </summary>
public sealed class LogLevelSettingConfiguration : IEntityTypeConfiguration<LogLevelSetting>
{
    /// <summary>The longest level name stored (<c>Information</c> is eleven characters).</summary>
    private const int LevelMaxLength = 16;

    /// <summary>Configures the table, key, and columns for <see cref="LogLevelSetting"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<LogLevelSetting> builder)
    {
        builder.ToTable("LogLevelSettings");
        builder.HasKey(setting => setting.Id);

        // Never generated: the key is the constant LogLevelSetting.SingletonId, which is what makes a
        // second row impossible rather than merely unusual.
        builder.Property(setting => setting.Id)
            .ValueGeneratedNever();

        // Stored by name, not by number: an operator reading the table with psql sees "Debug", and a
        // reordering of the enum could not silently turn a stored choice into another level.
        builder.Property(setting => setting.Level)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(LevelMaxLength);

        builder.Property(setting => setting.ChangedAt)
            .IsRequired();
    }
}
