using Microsoft.EntityFrameworkCore;

namespace Maran.Host.Tests.Cli.Fixtures;

/// <summary>A context owning the schema <c>sales</c> and no tables, never connected: only its model is read.</summary>
internal sealed class SalesTestDbContext : DbContext
{
    /// <summary>Creates the context.</summary>
    /// <param name="options">The options it was registered with.</param>
    public SalesTestDbContext(DbContextOptions<SalesTestDbContext> options)
        : base(options)
    {
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("sales");
    }
}
