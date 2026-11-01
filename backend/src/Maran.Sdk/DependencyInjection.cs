using Maran.Sdk.Alerts;

namespace Maran.Sdk;

/// <summary>
/// Registration entry point of the Sdk: every project exposes exactly one <c>Add&lt;Project&gt;</c>
/// method and the Host's <c>Program.cs</c> reads as a table of those calls (rules/csharp.md
/// "Cross-cutting infrastructure").
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the Sdk's shared facilities: <see cref="OperatorAlertMailer"/>, scoped because the
    /// recipient window it reads is a module's scoped service.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddSdk(this IServiceCollection services)
    {
        services.AddScoped<OperatorAlertMailer>();
        return services;
    }
}
