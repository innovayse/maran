using Maran.Modules.Licensing.Options;
using Maran.Modules.Licensing.Resources;
using Maran.Modules.Licensing.Services;
using Maran.SharedKernel.Interfaces;
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

    /// <summary>Builds the source that says whether a licence is close to its expiry.</summary>
    /// <param name="clock">The clock the warning is judged against.</param>
    /// <param name="thresholdDays">The configured thresholds; null for the defaults (30, 14 and 3 days).</param>
    /// <returns>The source.</returns>
    public static LicenceExpiryWarningSource ExpiryWarnings(IClock clock, int[]? thresholdDays = null)
    {
        return new LicenceExpiryWarningSource(
            clock,
            new OptionsWrapper<LicenceExpiryWarningOptions>(
                new LicenceExpiryWarningOptions { ExpiryWarningDays = thresholdDays }));
    }

    /// <summary>Builds the reader of the server id and the claim link over a fixed machine id.</summary>
    /// <param name="machineId">The machine id the host reports, or null for a host whose id cannot be read.</param>
    /// <param name="claimPageBaseUrl">The configured origin of the claim page; the documented default when null.</param>
    /// <returns>The reader.</returns>
    public static LicenceServerFactsSource ServerFacts(string? machineId = null, string? claimPageBaseUrl = null)
    {
        var options = new OptionsWrapper<LicenceClaimOptions>(
            claimPageBaseUrl is null
                ? new LicenceClaimOptions()
                : new LicenceClaimOptions { ClaimPageBaseUrl = claimPageBaseUrl });

        return new LicenceServerFactsSource(new FixedServerIdentitySource(machineId), options);
    }
}
