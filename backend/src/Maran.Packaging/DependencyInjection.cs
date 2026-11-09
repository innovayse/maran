using Maran.Packaging.Options;
using Maran.Packaging.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maran.Packaging;

/// <summary>
/// Registration entry point of the packaging library: every project exposes exactly one
/// <c>Add&lt;Project&gt;</c> method (rules/csharp.md "Cross-cutting infrastructure").
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the archive reader and the package verifier with the default caps. It registers NO
    /// signature verifier: the key is the one thing the caller supplies (an
    /// <c>IIpmSignatureVerifier</c> built from a public key it pins), so a host that resolves
    /// <see cref="IpmPackageVerifier"/> without registering one fails loudly instead of trusting a key
    /// this library carries.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPackaging(this IServiceCollection services)
    {
        services.TryAddSingleton(IpmLimits.Default);
        services.TryAddSingleton<IpmReader>();
        services.TryAddSingleton<IpmPackageVerifier>();
        return services;
    }
}
