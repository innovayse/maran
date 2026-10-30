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

    /// <summary>The embedded resource base name of the Sdk's <c>Resources/ValidationMessages*.resx</c>.</summary>
    private const string SdkValidationMessagesResourceBaseName = "Maran.Sdk.Resources.ValidationMessages";

    /// <summary>Adds the localization services every module's typed resource lookup depends on.</summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPanelLocalization(this IServiceCollection services)
    {
        services.AddLocalization();

        // The Host produces failures of its own that belong to no module — HostUnexpectedError
        // from ExceptionMiddleware, HostRateLimited from the rate limiter's rejection handler, and
        // HostForbidden / HostTwoFactorSetupRequired from ProblemDetailsAuthorizationResultWriter.
        // Their resource manager joins the same shared pool the panel-wide
        // ResxErrorTextProvider resolves against, so all of them are localized exactly like a module's
        // codes rather than falling back to the raw code (rules/csharp.md "The backend owns all
        // user-facing message text").
        services.AddSingleton(
            new ResourceManager(ErrorMessagesResourceBaseName, typeof(LocalizationExtensions).Assembly));

        // The Sdk's PagedQueryValidator and its paging model binders refuse a malformed list
        // request with codes of their own (ListPageOutOfRange, ListSearchTooLong, ListSortUnknown,
        // ...). The Sdk has no registration entry point of its own, and the composition root is the
        // one place that knows it is loaded, so its table joins the shared pool here — without it
        // every list's 400 would render the bare code.
        services.AddSingleton(
            new ResourceManager(
                SdkValidationMessagesResourceBaseName,
                typeof(Maran.Sdk.Validators.PagedQueryValidator<,>).Assembly));

        return services;
    }
}
