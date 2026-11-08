using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Events;

namespace Maran.Modules.Accounts.Tests.Services;

/// <summary>
/// Behavioral contract of <see cref="AccountPlanChangeAnnouncer"/>: one inline event per account, and
/// a subscriber's failure is logged and never thrown, because the change is already committed (issue #138).
/// </summary>
public sealed class AccountPlanChangeAnnouncerTests
{
    /// <summary>Each account is announced once, in order.</summary>
    [Fact]
    public async Task Each_account_is_announced_once_in_order()
    {
        var bus = new StubMessageBus();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await new AccountPlanChangeAnnouncer(bus, new RecordingLogger<AccountPlanChangeAnnouncer>())
            .AnnounceAsync([first, second], CancellationToken.None);

        Assert.Equal(2, bus.Invoked.Count);
        Assert.Equal(new AccountPlanChanged(first), bus.Invoked[0]);
        Assert.Equal(new AccountPlanChanged(second), bus.Invoked[1]);
    }

    /// <summary>No account announces nothing.</summary>
    [Fact]
    public async Task No_account_announces_nothing()
    {
        var bus = new StubMessageBus();

        await new AccountPlanChangeAnnouncer(bus, new RecordingLogger<AccountPlanChangeAnnouncer>())
            .AnnounceAsync([], CancellationToken.None);

        Assert.Empty(bus.Invoked);
    }

    /// <summary>A panel with no subscriber is not announced to and logs no failure.</summary>
    [Fact]
    public async Task A_panel_with_no_subscriber_is_not_announced_to_and_logs_nothing()
    {
        var bus = new StubMessageBus { HasSubscribers = false };
        var logger = new RecordingLogger<AccountPlanChangeAnnouncer>();

        await new AccountPlanChangeAnnouncer(bus, logger).AnnounceAsync([Guid.NewGuid()], CancellationToken.None);

        Assert.Empty(bus.Invoked);
        Assert.Empty(logger.Messages);
    }

    /// <summary>A subscriber that refuses is logged and the announcement does not throw.</summary>
    [Fact]
    public async Task A_subscriber_that_refuses_is_logged_and_the_announcement_does_not_throw()
    {
        var bus = new StubMessageBus(new InvalidOperationException("agent unreachable"));
        var logger = new RecordingLogger<AccountPlanChangeAnnouncer>();
        var accountId = Guid.NewGuid();

        await new AccountPlanChangeAnnouncer(bus, logger).AnnounceAsync([accountId], CancellationToken.None);

        Assert.Single(bus.Invoked);
        Assert.Contains(logger.Messages, message =>
        {
            return message.Contains(accountId.ToString(), StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>A failure for one account does not stop the next from being announced.</summary>
    [Fact]
    public async Task A_failure_for_one_account_does_not_stop_the_next_from_being_announced()
    {
        var bus = new StubMessageBus(new InvalidOperationException("agent unreachable"));

        await new AccountPlanChangeAnnouncer(bus, new RecordingLogger<AccountPlanChangeAnnouncer>())
            .AnnounceAsync([Guid.NewGuid(), Guid.NewGuid()], CancellationToken.None);

        Assert.Equal(2, bus.Invoked.Count);
    }

    /// <summary>A cancelled request stops the announcements after the first failure and still does not throw.</summary>
    [Fact]
    public async Task A_cancelled_request_stops_the_announcements_and_does_not_throw()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var bus = new StubMessageBus(new OperationCanceledException(source.Token));

        await new AccountPlanChangeAnnouncer(bus, new RecordingLogger<AccountPlanChangeAnnouncer>())
            .AnnounceAsync([Guid.NewGuid(), Guid.NewGuid()], source.Token);

        Assert.Single(bus.Invoked);
    }
}
