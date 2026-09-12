using System.Resources;
using Maran.Modules.Ftp.Interfaces;
using Maran.Modules.Ftp.Persistence;
using Maran.Modules.Ftp.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ftp;

/// <summary>
/// The Ftp module's entry point (rules/csharp.md "Canonical backend layout"). Registers
/// <see cref="FtpDbContext"/> against the <c>ftp</c> PostgreSQL schema and contributes the module's
/// controllers to the Host's routing. Owns this server's own FTPS daemon: the switch that turns it
/// on, what it was configured with, and the observation the panel reports.
/// </summary>
public sealed class FtpModule : IPanelModule
{
    /// <summary>Configuration key under which the panel's connection string lives.</summary>
    private const string ConnectionStringName = "Panel";

    /// <summary>The embedded resource base name of <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Modules.Ftp.Resources.ErrorMessages";

    /// <summary>The embedded resource base name of <c>Resources/DisplayNames*.resx</c>.</summary>
    private const string DisplayNamesResourceBaseName = "Maran.Modules.Ftp.Resources.DisplayNames";

    /// <inheritdoc />
    public string Name
    {
        get
        {
            return Manifest.Id;
        }
    }

    /// <inheritdoc />
    public Manifest Manifest
    {
        get
        {
            return FtpManifest.Instance;
        }
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? string.Empty;

        // Scoped by default. Nothing this module stores is tenant-owned — there is one settings row
        // for the whole installation — but the context is still per-request, because a singleton
        // DbContext is shared mutable state across concurrent requests whatever it holds.
        services.AddDbContext<FtpDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // Scoped, because it reads the request's own ICurrentUser for the journal's actor.
        services.AddScoped<FtpAuditJournal>();

        // Scoped, because it takes a transaction on the request's own DbContext connection: the
        // advisory lock it holds is scoped to that transaction and must be released by its commit.
        services.AddScoped<IFtpUserSlotGate, FtpUserSlotGate>();

        // Scoped for the journal it holds. Its work is unattended — it runs for a certificate the
        // Ssl module installed, not for a caller — which is exactly why it exists as one service
        // rather than as logic inside whatever happens to notice.
        services.AddScoped<FtpsTlsReloader>();

        // The shared resource pool the panel-wide ResxErrorTextProvider resolves error codes and
        // Manifest.DisplayNameKey against. Module-internal lookups inject IStringLocalizer<T>
        // directly instead.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(FtpModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(FtpModule).Assembly));
    }
}
