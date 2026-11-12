using System.Resources;
using Maran.Host.Demo.Adapters;
using Maran.Host.Demo.Guard;
using Maran.Host.Demo.Options;
using Maran.Host.Demo.Pseudonymisation;
using Maran.Host.Demo.Services;
using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Notifications.Interfaces;
using Maran.Modules.Ssl.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Maran.Host.Demo.Extensions;

/// <summary>Registers the demo guard and places its write gate in the pipeline.</summary>
public static class DemoGuardExtensions
{
    /// <summary>The embedded resource base name of the demo's <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Host.Demo.Resources.ErrorMessages";

    /// <summary>
    /// Registers the refusal table, the mutation budgets, the address stand-ins, the demo's text, and the
    /// replacements for the mailer, the ACME client, the licence signature verifier and the licence storage.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configuration">The configuration holding the <c>Demo</c> section.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// <b>Must be called AFTER <c>AddPanelModules</c></b> (which registers the Licensing module with the
    /// others). The replacements remove the module's own
    /// registration, and the last registration of a service wins, so called before either the module's would
    /// be added over them and the real mailer, the real ACME client, the real verifier and the licence
    /// file storage would be the ones resolved. <c>DemoCompositionTests</c> resolves each from the composed
    /// provider so the order cannot slip unseen.
    /// </remarks>
    public static IServiceCollection AddDemoGuard(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DemoOptions>()
            .Bind(configuration.GetSection(DemoOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DemoOptions>, DemoOptionsValidator>();

        services.AddSingleton(DemoEndpointPolicy.Default);
        services.AddSingleton<DemoProtectedResources>();
        services.AddSingleton(DemoTextScreen.Default);
        services.AddSingleton<DemoWriteGuard>();
        services.AddSingleton<DemoMutationBudget>();
        services.AddSingleton<DemoSignInBudget>();
        services.AddSingleton<DemoAuditAllowance>();
        services.AddScoped<DemoAuditJournal>();
        services.AddSingleton(_ => { return new ClientPseudonymiser(); });

        services.Configure<MvcOptions>(options => { options.Filters.Add<DemoInitialPasswordFilter>(); });
        services.Configure<MvcOptions>(options => { options.Filters.Add<DemoContentFilter>(); });

        services.AddSingleton<DemoLicenceKey>();
        services.AddSingleton<CapturingMailer>();
        services.Replace(
            ServiceDescriptor.Singleton<IMailer>(
                provider => { return provider.GetRequiredService<CapturingMailer>(); }));
        services.Replace(ServiceDescriptor.Singleton<IAcmeClient, SimulatedAcmeClient>());
        services.Replace(ServiceDescriptor.Singleton<ILicenceSignatureVerifier, DemoLicenceSignatureVerifier>());

        // No call to the licence service from a public demonstration: its lease renewal finds no service.
        services.Replace(ServiceDescriptor.Singleton<ILeaseServiceClient, DemoLeaseServiceClient>());

        // No licence file IO in the demo: the installed licence is kept in memory.
        services.AddSingleton<DemoLicenceStore>();
        services.Replace(ServiceDescriptor.Singleton<ILicenceRawTextSource>(
            provider => { return provider.GetRequiredService<DemoLicenceStore>(); }));
        services.Replace(ServiceDescriptor.Singleton<ILicenceWriter>(
            provider => { return provider.GetRequiredService<DemoLicenceStore>(); }));

        // Nor does the licence watcher write its expiry note to a state file: the announcement is kept in memory.
        services.Replace(ServiceDescriptor.Singleton<ILicenceExpiryAnnouncementStore, DemoLicenceExpiryAnnouncementStore>());

        // Joins the shared pool the panel-wide error text provider resolves against, exactly as the
        // Host's own table does, so the demo's codes are localized and not shown as raw codes.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(DemoGuardExtensions).Assembly));

        return services;
    }

    /// <summary>Adds the write gate: the refusal table and the mutation budgets.</summary>
    /// <param name="app">The application being built.</param>
    /// <returns>The same application, for chaining.</returns>
    /// <remarks>
    /// <b>Must come after authentication, authorization and <c>UseClientPseudonym</c></b>, and before the
    /// endpoints run, so that an unauthenticated call is still a 401, the journal knows who was refused, and
    /// the visitor key is a stand-in. The matched endpoint is read from the request, so it must also come
    /// after routing.
    /// </remarks>
    public static WebApplication UseDemoWriteGuard(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<DemoWriteGuardMiddleware>();
        return app;
    }
}
