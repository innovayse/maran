using Maran.Modules.Monitoring.Models;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Monitoring.Persistence.Configurations;

/// <summary>
/// Declares <see cref="MetricBucketRow"/> to EF Core as a keyless read model, so the chart's raw SQL
/// can be materialised into it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Keyless, and mapped to no table.</b> The rows this type carries are computed by
/// <c>date_bin</c> at read time and exist nowhere on disk: there is no key that would identify one,
/// and nothing ever inserts, updates or tracks it. <c>ToView(null)</c> is what keeps a migration
/// from proposing a table for it — without that line the model believes it owns storage and the next
/// migration creates an empty table nothing writes to.
/// </para>
/// <para>
/// It exists at all because EF Core's <c>SqlQueryRaw</c> materialises scalar types only; a
/// multi-column result needs a type the model knows about. A keyless entity is the sanctioned way to
/// say "this is a shape I read, not a thing I store".
/// </para>
/// </remarks>
public sealed class MetricBucketRowConfiguration : IEntityTypeConfiguration<MetricBucketRow>
{
    /// <summary>Marks the type keyless and detaches it from any table.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<MetricBucketRow> builder)
    {
        builder.HasNoKey();
        builder.ToView((string?)null);
    }
}
