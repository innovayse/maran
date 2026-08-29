namespace Maran.Host.Extensions;

/// <summary>
/// Registers ASP.NET Core's typed resource localization (<see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/>),
/// which every module's <c>Resources/Messages.cs</c> marker class resolves text through
/// (rules/csharp.md "Resources are reached through <c>IStringLocalizer&lt;T&gt;</c>"). The request
/// culture itself comes from <c>RequestLocalizationMiddleware</c>, which sets the ambient
/// <see cref="System.Globalization.CultureInfo.CurrentUICulture"/> the localizer reads — this
/// registration only makes the typed lookup mechanism available.
/// </summary>
public static class LocalizationExtensions
{
    /// <summary>Adds the localization services every module's typed resource lookup depends on.</summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPanelLocalization(this IServiceCollection services)
    {
        return services.AddLocalization();
    }
}
