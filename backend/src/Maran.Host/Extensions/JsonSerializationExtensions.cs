using Maran.Sdk.Serialization;

namespace Maran.Host.Extensions;

/// <summary>
/// Panel-wide JSON serialization settings. Enums (e.g. <c>Maran.Sdk.Contracts.LicenceTier</c>,
/// <c>Maran.Modules.Accounts.Domain.Enums.AccountStatus</c>) serialize as their camelCase member name
/// (matching the panel's camelCase property naming) rather than their underlying number, so the
/// wire contract the SPA depends on stays readable and stable across releases even as enum members
/// are added (rules/csharp.md "Additive evolution"). Both API surfaces the panel exposes — MVC
/// controllers and ASP.NET Core minimal API endpoints — have their own independent JSON options,
/// so both are configured here.
///
/// <see cref="Maran.SharedKernel.Security.SensitiveStringJsonConverter"/> is registered alongside, so a freshly minted password
/// can reach the one response that shows it while still rendering as <c>[redacted]</c> everywhere
/// else. Registering it on ONE surface would be the worse failure: the value would silently
/// serialize as the mask on the other, and the customer would be shown a password that does not
/// work.
///
/// The converters come from <see cref="PanelJsonOptions"/>, which the server-sent-event stream
/// serializes with as well, so the two transports cannot spell a value differently.
/// </summary>
public static class JsonSerializationExtensions
{
    /// <summary>Adds controllers and configures string-enum JSON serialization for both API surfaces.</summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPanelJsonSerialization(this IServiceCollection services)
    {
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                PanelJsonOptions.Apply(options.JsonSerializerOptions);
            });

        services.ConfigureHttpJsonOptions(options =>
        {
            PanelJsonOptions.Apply(options.SerializerOptions);
        });

        return services;
    }
}
