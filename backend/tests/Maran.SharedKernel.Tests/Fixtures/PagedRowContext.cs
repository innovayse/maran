using Microsoft.EntityFrameworkCore;

namespace Maran.SharedKernel.Tests.Fixtures;

/// <summary>An in-memory EF Core context over <see cref="PagedRow"/>, private to one test.</summary>
public sealed class PagedRowContext : DbContext
{
    /// <summary>The rows.</summary>
    public DbSet<PagedRow> Rows
    {
        get { return Set<PagedRow>(); }
    }

    /// <summary>Creates a context on a database no other test shares.</summary>
    public PagedRowContext()
        : base(new DbContextOptionsBuilder<PagedRowContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options)
    {
    }
}
