using Maran.Host.Security;

namespace Maran.Host.Extensions;

/// <summary>
/// Registers the panel's security services. Authentication itself is not built yet; what is
/// registered here is the minimum that lets the rest of the pipeline run without pretending anyone
/// is signed in.
/// </summary>
public static class SecurityExtensions
{
    /// <summary>Registers the current-user accessor.</summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPanelSecurity(this IServiceCollection services)
    {
        // Scoped rather than singleton: the real implementation will read the request's principal,
        // so registering it per request now means swapping the type later changes nothing else.
        services.AddScoped<ICurrentUser, UnauthenticatedCurrentUser>();
        return services;
    }
}
