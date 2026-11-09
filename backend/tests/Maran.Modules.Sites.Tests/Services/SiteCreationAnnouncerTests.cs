using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Events;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Sites.Tests.Services;

/// <summary>
/// Behavioral contract of <see cref="SiteCreationAnnouncer"/>: one inline <see cref="SiteCreated"/> per
/// creation, nothing announced to a panel with no subscriber, and a subscriber's failure logged and never
/// thrown, because the site is already created.
/// </summary>
public sealed class SiteCreationAnnouncerTests
{
    /// <summary>The created site is announced once, inline, with the facts a subscriber needs.</summary>
    [Fact]
    public async Task The_created_site_is_announced_once_inline()
    {
        var bus = new RecordingMessageBus();
        var created = new SiteCreated(Guid.NewGuid(), Guid.NewGuid(), "example.com");

        await new SiteCreationAnnouncer(bus, new RecordingLogger<SiteCreationAnnouncer>())
            .AnnounceAsync(created, CancellationToken.None);

        Assert.Equal(created, Assert.Single(bus.Invoked));
        Assert.Empty(bus.Published);
    }

    /// <summary>A panel with no subscriber is not announced to and logs no failure.</summary>
    [Fact]
    public async Task A_panel_with_no_subscriber_is_not_announced_to_and_logs_nothing()
    {
        var bus = new RecordingMessageBus { HasSubscribers = false };
        var logger = new RecordingLogger<SiteCreationAnnouncer>();

        await new SiteCreationAnnouncer(bus, logger)
            .AnnounceAsync(new SiteCreated(Guid.NewGuid(), Guid.NewGuid(), "example.com"), CancellationToken.None);

        Assert.Empty(bus.Invoked);
        Assert.Empty(logger.Entries);
    }

    /// <summary>A subscriber that refuses is logged by the site's id and the announcement does not throw.</summary>
    [Fact]
    public async Task A_subscriber_that_refuses_is_logged_and_the_announcement_does_not_throw()
    {
        var bus = new RecordingMessageBus { Refusal = new InvalidOperationException("agent unreachable") };
        var logger = new RecordingLogger<SiteCreationAnnouncer>();
        var siteId = Guid.NewGuid();

        await new SiteCreationAnnouncer(bus, logger)
            .AnnounceAsync(new SiteCreated(siteId, Guid.NewGuid(), "example.com"), CancellationToken.None);

        Assert.Single(bus.Invoked);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(siteId.ToString(), entry.Message, StringComparison.OrdinalIgnoreCase);
    }
}
