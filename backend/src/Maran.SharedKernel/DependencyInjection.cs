using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Localization;
using Maran.SharedKernel.Security;
using Maran.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.SharedKernel;

/// <summary>
/// Registration entry point of the SharedKernel: every project exposes exactly
/// one <c>Add&lt;Project&gt;</c> method and the Host's <c>Program.cs</c> reads as
/// a table of those calls (rules/csharp.md "Cross-cutting infrastructure").
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the kernel's primitives: the production clock, the Argon2id password hasher, and
    /// the generic .resx-backed <see cref="IErrorTextProvider"/> that resolves against every
    /// module's own resource manager (each module registers its own via <c>ConfigureServices</c>).
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddSharedKernel(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IErrorTextProvider, ResxErrorTextProvider>();
        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        return services;
    }
}
