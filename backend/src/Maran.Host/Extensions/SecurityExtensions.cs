using Maran.Host.Configuration;
using Maran.Host.Middleware;
using Maran.Host.Security;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Options;

namespace Maran.Host.Extensions;

/// <summary>
/// Registers the panel's security services: who the caller is, the cipher protecting secrets
/// at rest, and the keyed fingerprint of a request.
/// </summary>
public static class SecurityExtensions
{
    /// <summary>Registers the current-user accessor, the encryption service and the request fingerprint.</summary>
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

        // The keyed fingerprint the Idempotency-Key filter stores in place of a plain digest of a request
        // body, which can carry a password. Its MAC key is derived from the same panel secret.
        services.AddSingleton<IIdempotencyFingerprint>(provider =>
        {
            return new HmacIdempotencyFingerprint(provider.GetRequiredService<IOptions<SecurityOptions>>().Value.EncryptionKey);
        });

        return services;
    }
}
