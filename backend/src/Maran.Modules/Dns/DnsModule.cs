using System.Resources;
using Maran.Modules.Dns.Interfaces;
using Maran.Modules.Dns.Options;
using Maran.Modules.Dns.Persistence;
using Maran.Modules.Dns.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Dns;

/// <summary>
/// The Dns module's entry point (rules/csharp.md "Canonical backend layout"). Registers
/// <see cref="DnsDbContext"/> against the <c>dns</c> PostgreSQL schema and contributes the module's
/// controllers to the Host's routing. Owns this server's authoritative name server: the switch that turns
/// it on, the settings every zone is published with, the zones accounts own and the records in them.
/// </summary>
public sealed class DnsModule : IPanelModule
{
    /// <summary>Configuration key under which the panel's connection string lives.</summary>
    private const string ConnectionStringName = "Panel";

    /// <summary>The embedded resource base name of <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Modules.Dns.Resources.ErrorMessages";

    /// <summary>The embedded resource base name of <c>Resources/DisplayNames*.resx</c>.</summary>
    private const string DisplayNamesResourceBaseName = "Maran.Modules.Dns.Resources.DisplayNames";

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
            return DnsManifest.Instance;
        }
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? string.Empty;

        // Scoped by default: the context reads the request's own principal for its tenant filter, and a
        // singleton DbContext is shared mutable state across concurrent requests whatever it holds.
        services.AddDbContext<DnsDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // A singleton, because it holds no state: it opens its own scope per call, so the Host can run the
        // module's migrations through the root provider (rules/architecture.md "The database evolves the same way").
        services.AddSingleton<IModuleMigrations, DnsModuleMigrations>();

        // The operator's per-zone record cap, validated at start-up so a misconfigured server does not boot.
        services.AddOptions<DnsOptions>()
            .Bind(configuration.GetSection(DnsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Scoped, because it reads the request's own tenant-scoped DbContext: this module's answer to the
        // personal-data export, an allow-list of fields.
        services.AddScoped<IPersonalDataProvider, DnsPersonalDataProvider>();

        // Scoped, because it reads the request's own ICurrentUser for the journal's actor.
        services.AddScoped<DnsAuditJournal>();

        // Scoped, because they take a transaction on the request's own DbContext connection: the advisory
        // lock each holds is scoped to that transaction and must be released by its commit.
        services.AddScoped<IDnsZoneSlotGate, DnsZoneSlotGate>();
        services.AddScoped<IDnsZoneLock, DnsZoneLock>();
        services.AddScoped<DnsZoneEditor>();

        // Scoped, because it reads through the request's context with the tenant filter deliberately off
        // for exactly one question (see ZoneNameDirectory).
        services.AddScoped<ZoneNameDirectory>();

        // This module's answer to how many zones an account owns, for the Accounts module's resource
        // summary. Scoped, because it reads through the request-scoped, tenant-filtered context.
        services.AddScoped<IAccountResourceCounter, DnsAccountResourceCounter>();

        // The shared resource pool the panel-wide ResxErrorTextProvider resolves error codes and
        // Manifest.DisplayNameKey against. Module-internal lookups inject IStringLocalizer<T> directly.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(DnsModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(DnsModule).Assembly));
    }
}
