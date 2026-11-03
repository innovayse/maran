using Maran.Modules.Cron.Resources;
using Maran.Modules.Cron.Services;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Cron.Tests.TestSupport;

/// <summary>Builds the run display-name resolver over this module's REAL resource files.</summary>
public static class CronTestNames
{
    /// <summary>Builds the resolver as the panel does, reading <c>Resources/DisplayNames*.resx</c>.</summary>
    /// <returns>The resolver, answering in the thread's current UI culture.</returns>
    public static CronRunDisplayNames Runs()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
            NullLoggerFactory.Instance);

        return new CronRunDisplayNames(new StringLocalizer<DisplayNames>(factory));
    }
}
