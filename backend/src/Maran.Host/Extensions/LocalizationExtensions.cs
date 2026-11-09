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

    /// <summary>The embedded resource base name of the Sdk's <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string SdkErrorMessagesResourceBaseName = "Maran.Sdk.Resources.ErrorMessages";

    /// <summary>The embedded resource base name of the packaging library's <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string PackagingErrorMessagesResourceBaseName = "Maran.Packaging.Resources.ErrorMessages";

    /// <summary>Adds the localization services every module's typed resource lookup depends on.</summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPanelLocalization(this IServiceCollection services)
    {
        services.AddLocalization();

        // The Host produces failures of its own that belong to no module — HostUnexpectedError
        // from ExceptionMiddleware, HostRateLimited from the rate limiter's rejection handler, and
        // HostForbidden / HostPasswordChangeRequired / HostTwoFactorSetupRequired / HostViewAsNotAllowed from
        // ProblemDetailsAuthorizationResultWriter.
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

        // The Sdk's module-loading seam answers with codes of its own (MarketplaceNotAvailable),
        // defined in its ErrorMessages table; that table joins the same pool so the code renders
        // as a sentence in the caller's language and not as the bare code.
        services.AddSingleton(
            new ResourceManager(
                SdkErrorMessagesResourceBaseName,
                typeof(Maran.Sdk.Validators.PagedQueryValidator<,>).Assembly));

        // The packaging library refuses a module package with typed codes of its own (IpmSignatureInvalid,
        // IpmDowngradeRefused, ...) that an install will carry into a task failure and an API answer.
        // The library has no module to register its table, so the composition root joins it to the
        // shared pool here; without it every refusal of a package would render the bare code.
        services.AddSingleton(
            new ResourceManager(
                PackagingErrorMessagesResourceBaseName,
                typeof(Maran.Packaging.Services.IpmReader).Assembly));

        return services;
    }
}
