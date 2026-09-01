using Maran.Modules.Sites.Common.Options;
using Microsoft.Extensions.Options;

namespace Maran.Host.Tests.Configuration;

/// <summary>
/// Behavioral contract of the startup validation of
/// <see cref="SiteLogOptions"/>: a heartbeat interval that could not keep a
/// stream alive through a proxy must fail the boot, not the first quiet log.
/// </summary>
/// <remarks>
/// The bound exists because of a defect one layer out, and that is why it is worth a test of its
/// own. The panel's own installed vhost sets <c>proxy_read_timeout 300s</c> and an unconfigured
/// nginx uses 60 s; a heartbeat longer than either is not a slow heartbeat but a stream torn down
/// with no <c>end</c> event, which the browser can only report as a truncation of a healthy log.
/// The setting's range once reached 600 — twice the panel's own timeout — so the validation
/// admitted a configuration that reintroduced the exact failure the heartbeat was added to close.
/// </remarks>
public sealed class SiteLogOptionsTests
{
    /// <summary>A heartbeat longer than a proxy read timeout fails startup.</summary>
    [Fact]
    public void A_heartbeat_longer_than_a_proxy_read_timeout_fails_startup()
    {
        using var factory = new PanelTestFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Sites:Logs:HeartbeatSeconds", "300");
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
        {
            return _ = factory.Services;
        });

        Assert.Contains("HeartbeatSeconds", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A heartbeat of zero fails startup rather than beating continuously.</summary>
    [Fact]
    public void A_heartbeat_of_zero_fails_startup_rather_than_beating_without_pause()
    {
        using var factory = new PanelTestFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Sites:Logs:HeartbeatSeconds", "0");
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
        {
            return _ = factory.Services;
        });

        Assert.Contains("HeartbeatSeconds", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>The shipped default is accepted and the host starts.</summary>
    [Fact]
    public void The_shipped_default_heartbeat_is_accepted()
    {
        using var factory = new PanelTestFactory();

        Assert.NotNull(factory.Services.GetService(typeof(IOptions<SiteLogOptions>)));
    }
}
