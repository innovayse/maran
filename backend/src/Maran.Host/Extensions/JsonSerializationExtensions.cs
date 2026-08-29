using System.Text.Json;
using System.Text.Json.Serialization;

namespace Maran.Host.Extensions;

/// <summary>
/// Panel-wide JSON serialization settings. Enums (e.g. <c>Maran.Sdk.Contracts.LicenceTier</c>,
/// <c>Maran.Modules.Accounts.Domain.AccountStatus</c>) serialize as their camelCase member name
/// (matching the panel's camelCase property naming) rather than their underlying number, so the
/// wire contract the SPA depends on stays readable and stable across releases even as enum members
/// are added (rules/csharp.md "Additive evolution"). Both API surfaces the panel exposes — MVC
/// controllers and ASP.NET Core minimal API endpoints — have their own independent JSON options,
/// so both are configured here.
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
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

        return services;
    }
}
