using System.Text.Json;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Sites.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="SiteTrafficDay"/> onto the <c>sites.SiteTrafficDays</c> table.</summary>
/// <remarks>
/// The status and path lists are stored as one <c>jsonb</c> value each, as text the provider carries to a
/// <c>jsonb</c> column, rather than as owned types or child tables: nothing queries inside them (a read
/// takes whole days), and a child table per day would be thousands of rows a site for figures that are
/// only ever read together. Mapping them through a text converter also keeps the context free of the
/// provider's dynamic-JSON switch, which the shared data source does not enable.
/// </remarks>
public sealed class SiteTrafficDayConfiguration : IEntityTypeConfiguration<SiteTrafficDay>
{
    /// <summary>Configures the table, the composite key, the JSON columns and the cascade from the site.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<SiteTrafficDay> builder)
    {
        builder.ToTable("SiteTrafficDays");

        // A day exists once per site, so the pair is the key and no surrogate id sits beside it.
        builder.HasKey(day => new { day.SiteId, day.Date });

        builder.Property(day => day.Requests)
            .IsRequired();

        builder.Property(day => day.BodyBytes)
            .IsRequired();

        builder.Property(day => day.OtherPathRequests)
            .IsRequired();

        builder.Property(day => day.Statuses)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasConversion(
                statuses => Serialize(statuses),
                json => Deserialize<TrafficStatusCount>(json),
                ListComparer<TrafficStatusCount>());

        builder.Property(day => day.Paths)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasConversion(
                paths => Serialize(paths),
                json => Deserialize<TrafficPathCount>(json),
                ListComparer<TrafficPathCount>());

        // Cascade: a deleted site takes its figures with it.
        builder.HasOne(day => day.Site)
            .WithMany()
            .HasForeignKey(day => day.SiteId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    /// <summary>Compares and snapshots a stored list by its elements, so a change inside it is seen as a change.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <returns>The comparer EF Core's change tracking uses for the column.</returns>
    private static ValueComparer<IReadOnlyList<T>> ListComparer<T>()
    {
        return new ValueComparer<IReadOnlyList<T>>(
            (left, right) => left!.SequenceEqual(right!),
            list => HashOf(list),
            list => list.ToList());
    }

    /// <summary>Combines the hash of every element of a list, in order.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="items">The list to hash.</param>
    /// <returns>A hash that changes when an element does.</returns>
    private static int HashOf<T>(IReadOnlyList<T> items)
    {
        var hash = new HashCode();
        foreach (var item in items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    /// <summary>Writes a list as the JSON text stored in a <c>jsonb</c> column.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="items">The list to store.</param>
    /// <returns>The JSON array.</returns>
    private static string Serialize<T>(IReadOnlyList<T> items)
    {
        return JsonSerializer.Serialize(items);
    }

    /// <summary>Reads a stored JSON array back into a list.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="json">The stored JSON array.</param>
    /// <returns>The list; empty for a stored <c>null</c>.</returns>
    private static List<T> Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<List<T>>(json) ?? [];
    }
}
