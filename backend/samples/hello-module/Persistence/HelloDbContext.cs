using Maran.Samples.Hello.Domain.Entities;
using Maran.Samples.Hello.Persistence.Configurations;

namespace Maran.Samples.Hello.Persistence;

/// <summary>
/// The sample module's only database context. Owns the <c>hello</c> PostgreSQL schema exclusively: no
/// other module reads or writes it, and this module never reads another module's schema.
/// </summary>
/// <remarks>
/// It carries no tenant query filter and takes no <see cref="ICurrentUser"/>: a greeting belongs to the
/// server, and the one controller that reads it is administrator-only.
/// </remarks>
public sealed class HelloDbContext : DbContext
{
    /// <summary>The schema this context owns; equal to the module's id.</summary>
    public const string SchemaName = "hello";

    /// <summary>Creates the context.</summary>
    /// <param name="options">The provider and connection, from the module's registration.</param>
    public HelloDbContext(DbContextOptions<HelloDbContext> options)
        : base(options)
    {
    }

    /// <summary>The greetings, one row each.</summary>
    public DbSet<Greeting> Greetings
    {
        get
        {
            return Set<Greeting>();
        }
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new GreetingConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
