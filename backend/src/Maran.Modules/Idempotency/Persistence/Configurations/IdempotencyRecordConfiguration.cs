using Maran.Modules.Idempotency.Domain.Entities;
using Maran.Modules.Idempotency.Domain.Policies;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Idempotency.Persistence.Configurations;

/// <summary>Maps <see cref="IdempotencyRecord"/> onto <c>idempotency.IdempotencyRecords</c>.</summary>
public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming: PascalCase everywhere").
        builder.ToTable("IdempotencyRecords");
        builder.HasKey(record => record.Id);

        builder.Property(record => record.CallerKey)
            .IsRequired()
            .HasMaxLength(IdempotencyKeyPolicy.CallerKeyMaxLength);
        builder.Property(record => record.Key)
            .IsRequired()
            .HasMaxLength(IdempotencyKeyPolicy.KeyMaxLength);
        builder.Property(record => record.RequestHash)
            .IsRequired()
            .HasMaxLength(IdempotencyKeyPolicy.RequestHashLength);
        builder.Property(record => record.ReservationToken);
        builder.Property(record => record.State).IsRequired();
        builder.Property(record => record.StatusCode);

        // Unbounded text: the entity refuses to store a body over 256 KiB, so the bound is the entity's
        // and a column length here would only be a second, drifting copy of it.
        builder.Property(record => record.ResponseBody);
        builder.Property(record => record.ContentType).HasMaxLength(IdempotencyRecord.ContentTypeMaxLength);
        builder.Property(record => record.CreatedAt).IsRequired();
        builder.Property(record => record.ExpiresAt).IsRequired();

        // The identity of a record, and what makes a reservation atomic: of two concurrent inserts for
        // the same caller and key, the index lets exactly one in.
        builder.HasIndex(record => new { record.CallerKey, record.Key })
            .IsUnique()
            .HasDatabaseName("IX_IdempotencyRecords_CallerKey_Key");

        // The hourly purge reads "rows past their expiry, oldest first".
        builder.HasIndex(record => record.ExpiresAt)
            .HasDatabaseName("IX_IdempotencyRecords_ExpiresAt");
    }
}
