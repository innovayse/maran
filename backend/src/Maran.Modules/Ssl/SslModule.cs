using System.Resources;
using Maran.Modules.Ssl.Interfaces;
using Maran.Modules.Ssl.Jobs;
using Maran.Modules.Ssl.Options;
using Maran.Modules.Ssl.Persistence;
using Maran.Modules.Ssl.Services;
using Maran.Modules.Ssl.Validators;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Ssl;

/// <summary>
/// The Ssl module's entry point (rules/csharp.md "Canonical backend layout"). Registers
/// <see cref="SslDbContext"/> against the <c>ssl</c> PostgreSQL schema and contributes the module's
/// controllers to the Host's routing. Owns TLS certificates: ordering them, installing them, removing
/// them, and renewing them before they expire (spec §11).
/// </summary>
public sealed class SslModule : IPanelModule
{
    /// <summary>Configuration key under which the panel's connection string lives.</summary>
    private const string ConnectionStringName = "Panel";

    /// <summary>The embedded resource base name of <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Modules.Ssl.Resources.ErrorMessages";

    /// <summary>The embedded resource base name of <c>Resources/DisplayNames*.resx</c>.</summary>
    private const string DisplayNamesResourceBaseName = "Maran.Modules.Ssl.Resources.DisplayNames";

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
            return SslManifest.Instance;
        }
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? string.Empty;

        // Scoped by default, which is what the tenant filter requires: the context closes over the
        // request's own ICurrentUser, so a singleton context would freeze one caller's tenant scope
        // and serve it to everybody.
        services.AddDbContext<SslDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // Validated at startup rather than at the first order: an unusable directory URL or a missing
        // contact address is a configuration mistake, and finding it out when a customer clicks
        // "issue" means the customer is the one who found it (rules/security.md item 7).
        //
        // AcmeOptionsValidator carries the one check data annotations cannot express here: the
        // contact address is held to the panel's single definition of a valid address rather than
        // to a per-module annotation (rules/csharp.md "Cross-cutting infrastructure").
        services.AddSingleton<IValidateOptions<AcmeOptions>, AcmeOptionsValidator>();
        services.AddOptions<AcmeOptions>()
            .Bind(configuration.GetSection(AcmeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The named client the ACME client orders through. The Host attaches the outbound resilience
        // pipeline to this same name, so a call made through it is governed whether or not this
        // module remembers anything about timeouts.
        services.AddHttpClient(AcmeOptions.HttpClientName, client =>
        {
            // Infinite, deliberately, and not a missing setting. The Host attaches AcmePipeline to
            // this same name and that pipeline owns the deadline; leaving HttpClient.Timeout at its
            // hundred-second default — or setting it to the same configured value — puts a second
            // budget around the first, and the per-attempt timeout the operator configured then
            // silently cannot apply (rules/csharp.md "Every outbound call goes through a named
            // resilience pipeline": one pipeline, one deadline).
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        services.AddScoped<CertificateAuditJournal>();
        services.AddScoped<CertificateInstaller>();
        services.AddScoped<AcmeAccountStore>();
        services.AddScoped<IAcmeChallengeWriter, AcmeChallengeWriter>();
        services.AddScoped<IAcmeClient, AcmeClient>();
        services.AddScoped<CertificateRenewalHandler>();

        // The shared resource pool the panel-wide ResxErrorTextProvider resolves error codes and
        // Manifest.DisplayNameKey against. Module-internal lookups inject IStringLocalizer<T> instead.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(SslModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(SslModule).Assembly));
    }
}
