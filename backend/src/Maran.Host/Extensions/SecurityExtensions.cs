using Maran.Host.Configuration;
using Maran.Host.Middleware;
using Maran.Host.Security;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Options;

namespace Maran.Host.Extensions;

/// <summary>
/// Registers the panel's security services: who the caller is, and the cipher protecting secrets
/// at rest.
/// </summary>
public static class SecurityExtensions
{
    /// <summary>Registers the current-user accessor and the encryption service.</summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPanelSecurity(this IServiceCollection services)
    {
        // The accessor reads the ambient HttpContext, so both it and the accessor it wraps have to
        // be registered. Nothing had asked for either until the audit journal did — the same shape
        // of gap as the encryption service: a contract written, implemented, and never wired up.
        services.AddHttpContextAccessor();
        services.AddScoped<ICorrelationIdAccessor, CorrelationIdAccessor>();

        // Scoped: the principal is a property of the request, read from its validated token.
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        // The cipher every encrypted column goes through (SharedKernel's EncryptedStringConverter).
        // It is built here rather than in AddSharedKernel because the key is the Host's
        // configuration, and the kernel deliberately reads no configuration of its own.
        services.AddSingleton<IEncryptionService>(provider =>
        {
            return new AesGcmEncryptionService(provider.GetRequiredService<IOptions<SecurityOptions>>().Value.EncryptionKey);
        });

        return services;
    }
}
