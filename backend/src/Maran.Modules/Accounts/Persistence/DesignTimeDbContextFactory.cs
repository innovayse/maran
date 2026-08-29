using Microsoft.EntityFrameworkCore.Design;

namespace Maran.Modules.Accounts.Persistence;

/// <summary>
/// Lets EF Core design-time tooling (<c>dotnet ef migrations add</c>) construct
/// <see cref="AccountsDbContext"/> without booting the Host, which owns the real connection
/// string. Never used at runtime — the Host registers the context through DI instead.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AccountsDbContext>
{
    /// <summary>Builds a context pointed at a local design-time-only connection string.</summary>
    /// <param name="args">Unused; required by the <see cref="IDesignTimeDbContextFactory{TContext}"/> contract.</param>
    public AccountsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AccountsDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=maran_design;Username=postgres;Password=postgres");
        return new AccountsDbContext(optionsBuilder.Options);
    }
}
