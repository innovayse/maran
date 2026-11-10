using Maran.Modules.Marketplace.Interfaces;
using Maran.Modules.Marketplace.Options;
using Maran.Modules.Marketplace.Services;

namespace Maran.Modules.Marketplace.Extensions;

/// <summary>Registers the marketplace's outbound half.</summary>
public static class MarketplaceClientExtensions
{
    /// <summary>
    /// Registers the client, its transports, the signature checker, the catalogue cache and the package
    /// downloader, and the named HTTP client the real transport sends through.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Registering the named client is not contacting anyone.</b> Nothing here starts work: there is no
    /// hosted service, no timer and no start-up step, which is the property the outbound-gate tests pin.
    /// The client is created only when <see cref="MarketplaceHttpTransport"/> asks the factory for it, and
    /// it asks only after the options validated with an https host and the administrator's switch is on.
    /// Registration cannot be conditional on the options (they are validated lazily, so that a server that
    /// never uses the marketplace boots whatever <c>panel.env</c> says); the gate sits at first use instead.
    /// </para>
    /// <para>
    /// <b>The factory's own request logging is switched off for this client.</b> It writes the address of
    /// every request it sends, and the address of a package download is the grant: a log line holding it
    /// would be a credential in a file that outlives the request. The transport logs by attempt number
    /// alone instead (see its remarks), which is the only line this conversation leaves.
    /// </para>
    /// <para>
    /// The module's own registration binds <see cref="MarketplaceOptions"/> and registers
    /// <c>MarketplaceSettingsStore</c>; the Host attaches the timeout pipeline to the same client name
    /// (<c>MarketplacePipeline</c>), which a module cannot reference. The lease authorizer and the panel
    /// version come from the Sdk and the Host.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMarketplaceClient(this IServiceCollection services)
    {
        services.AddHttpClient(MarketplaceOptions.HttpClientName, MarketplaceHttpTransport.ConfigureClient)
            .ConfigurePrimaryHttpMessageHandler(MarketplaceHttpTransport.CreateHandler)
            .RemoveAllLoggers();

        services.AddSingleton<MarketplaceOptionsReader>();
        services.AddSingleton<CatalogueSignatureChecker>();
        services.AddSingleton<CatalogueCache>();
        services.AddSingleton<SignedResponseReader>();
        services.AddSingleton<DisabledMarketplaceTransport>();
        services.AddScoped<MarketplaceHttpTransport>();
        services.AddScoped<PackageDownloader>();
        services.AddScoped<IMarketplaceSwitch, MarketplaceSwitch>();
        services.AddScoped<IMarketplaceClient, MarketplaceClient>();

        return services;
    }
}
