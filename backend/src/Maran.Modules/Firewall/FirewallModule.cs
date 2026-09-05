using System.Resources;
using Maran.Modules.Firewall.Options;
using Maran.Modules.Firewall.Persistence;
using Maran.Modules.Firewall.Seeders;
using Maran.Modules.Firewall.Services;
using Maran.Modules.Firewall.Validators;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Firewall;

/// <summary>
/// The Firewall module's entry point (rules/csharp.md "Canonical backend layout"). Registers
/// <see cref="FirewallDbContext"/> against the <c>firewall</c> PostgreSQL schema and contributes the
/// module's controllers to the Host's routing. Owns the panel's side of the host firewall: which
/// ports are open, who is banned and why, and which addresses the automatic bans never touch
/// (spec §15).
/// </summary>
public sealed class FirewallModule : IPanelModule
{
    /// <summary>Configuration key under which the panel's connection string lives.</summary>
    private const string ConnectionStringName = "Panel";

    /// <summary>The embedded resource base name of <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Modules.Firewall.Resources.ErrorMessages";

    /// <summary>The embedded resource base name of <c>Resources/DisplayNames*.resx</c>.</summary>
    private const string DisplayNamesResourceBaseName = "Maran.Modules.Firewall.Resources.DisplayNames";

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
            return FirewallManifest.Instance;
        }
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? string.Empty;

        // Scoped, like every module context. This one carries no tenant filter — a firewall rule
        // belongs to the server, not to a customer — but a DbContext is not thread-safe and a
        // singleton one would be shared by every concurrent request.
        services.AddDbContext<FirewallDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // ValidateOnStart, and the refusal is the feature. A panel that came up with no SSH ports
        // would serve perfectly until the first firewall change and then cut off the operator's
        // session and itself, with no remote recovery — so the boot stops here instead, with a
        // message naming the environment variables in /etc/maran/panel.env that have to be fixed
        // (rules/csharp.md "Options validated at startup").
        services.AddSingleton<IValidateOptions<FirewallOptions>, FirewallOptionsValidator>();
        services.AddOptions<FirewallOptions>()
            .Bind(configuration.GetSection(FirewallOptions.SectionName))
            .ValidateOnStart();

        // Scoped, because it reads the request's own ICurrentUser for the journal's actor.
        services.AddScoped<FirewallAuditJournal>();
        services.AddScoped<WhitelistSeeder>();

        // Registered rather than constructed at its call sites, so the container is the list of
        // everything that answers "is this address exempt" and a reader auditing the ban paths finds
        // one registration instead of one `new` per caller.
        services.AddScoped<WhitelistGuard>();

        // The shared resource pool the panel-wide ResxErrorTextProvider resolves error codes and
        // Manifest.DisplayNameKey against. Module-internal lookups inject IStringLocalizer<T> instead.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(FirewallModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(FirewallModule).Assembly));
    }
}
