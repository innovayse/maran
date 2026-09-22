using Maran.Modules.Licensing.Resources;
using Maran.Modules.Licensing.Services;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Licensing.Tests.TestSupport;

/// <summary>
/// Builds <see cref="LicenceStatusDisplayNames"/> against the real embedded resx, the same shape as
/// the Databases module's <c>DatabasesTestContext.RefusalText()</c> — no fakes involved, so a missing
/// satellite assembly or an untranslated key is visible here rather than only in a browser.
/// </summary>
public static class LicensingTestContext
{
    /// <summary>Builds the resolver against the module's real, compiled-in resx resources.</summary>
    /// <returns>A resolver reading whatever culture is current when it is called.</returns>
    public static LicenceStatusDisplayNames StatusText()
    {
        var localizer = new StringLocalizer<DisplayNames>(
            new ResourceManagerStringLocalizerFactory(
                new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
                NullLoggerFactory.Instance));

        return new LicenceStatusDisplayNames(localizer);
    }
}
