using System.Resources;

namespace Maran.Host.Extensions;

/// <summary>
/// Registers ASP.NET Core's typed resource localization (<see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/>),
/// which every module's <c>Resources/Messages.cs</c> marker class resolves text through
/// (rules/csharp.md "Resources are reached through <c>IStringLocalizer&lt;T&gt;</c>"), together with
/// the Host's own <c>Resources/ErrorMessages*.resx</c> family. The request culture itself comes from
/// <c>RequestLocalizationMiddleware</c>, which sets the ambient
/// <see cref="System.Globalization.CultureInfo.CurrentUICulture"/> the localizer reads — this
/// registration only makes the typed lookup mechanism available.
/// </summary>
public static class LocalizationExtensions
{
    /// <summary>The embedded resource base name of the Host's <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Host.Resources.ErrorMessages";

    /// <summary>Adds the localization services every module's typed resource lookup depends on.</summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPanelLocalization(this IServiceCollection services)
    {
        services.AddLocalization();

        // The Host produces two failures of its own — HostUnexpectedError from ExceptionMiddleware
        // and HostRateLimited from the rate limiter's rejection handler — which belong to no
        // module. Their resource manager joins the same shared pool the panel-wide
        // ResxErrorTextProvider resolves against, so both are localized exactly like a module's
        // codes rather than falling back to the raw code (rules/csharp.md "The backend owns all
        // user-facing message text").
        services.AddSingleton(
            new ResourceManager(ErrorMessagesResourceBaseName, typeof(LocalizationExtensions).Assembly));

        return services;
    }
}
