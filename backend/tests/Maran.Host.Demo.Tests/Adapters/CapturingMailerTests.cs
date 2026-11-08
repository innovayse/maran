using Maran.Host.Demo.Adapters;
using Maran.Host.Demo.Tests.Fixtures;

namespace Maran.Host.Demo.Tests.Adapters;

/// <summary>The mailer sends nothing and keeps almost nothing.</summary>
public sealed class CapturingMailerTests
{
    /// <summary>A message is reported sent and its recipient and subject remembered with the time.</summary>
    [Fact]
    public async Task A_message_is_reported_sent_and_remembered_by_recipient_and_subject()
    {
        var clock = new FixedClock();
        var mailer = new CapturingMailer(clock);

        var result = await mailer.SendAsync("visitor@example.test", "Welcome", "body", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var mail = Assert.Single(mailer.Captured);
        Assert.Equal(new CapturedMail("visitor@example.test", "Welcome", clock.UtcNow), mail);
    }

    /// <summary>The body, which can carry a token, is never kept anywhere.</summary>
    [Fact]
    public async Task The_body_is_never_kept()
    {
        var mailer = new CapturingMailer(new FixedClock());

        await mailer.SendAsync(
            "a@example.test",
            "Reset",
            "https://panel.example/reset?token=SECRET-TOKEN",
            CancellationToken.None);

        Assert.DoesNotContain(
            "SECRET-TOKEN",
            string.Join('|', mailer.Captured.Select(mail => { return mail.ToString(); })),
            StringComparison.Ordinal);
    }

    /// <summary>The list is bounded: the oldest messages are dropped.</summary>
    [Fact]
    public async Task The_list_keeps_only_the_newest_messages()
    {
        var mailer = new CapturingMailer(new FixedClock());

        for (var i = 0; i < CapturingMailer.Capacity + 25; i++)
        {
            await mailer.SendAsync($"{i}@example.test", "s", "b", CancellationToken.None);
        }

        Assert.Equal(CapturingMailer.Capacity, mailer.Captured.Count);
        Assert.Equal("25@example.test", mailer.Captured[0].Recipient);
    }

    /// <summary>A huge recipient or subject is cut, so one message cannot hold the memory.</summary>
    [Fact]
    public async Task Long_fields_are_cut()
    {
        var mailer = new CapturingMailer(new FixedClock());

        await mailer.SendAsync(new string('r', 10_000), new string('s', 10_000), "b", CancellationToken.None);

        Assert.Equal(CapturingMailer.MaxFieldLength, mailer.Captured[0].Recipient.Length);
        Assert.Equal(CapturingMailer.MaxFieldLength, mailer.Captured[0].Subject.Length);
    }
}
