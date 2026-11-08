using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Mappers;
using Maran.Modules.Mail.Tests.TestSupport;

namespace Maran.Modules.Mail.Tests.Mappers;

/// <summary>Behavioral contract of <see cref="MailboxMapper"/> and <see cref="MailboxAgentMapper"/>.</summary>
public sealed class MailboxMapperTests
{
    /// <summary>Bytes are converted to whole mebibytes rounded up, never down.</summary>
    /// <param name="bytes">Bytes used.</param>
    /// <param name="expected">The mebibytes shown.</param>
    [Theory]
    [InlineData(0UL, 0L)]
    [InlineData(1UL, 1L)]
    [InlineData(1_048_575UL, 1L)]
    [InlineData(1_048_576UL, 1L)]
    [InlineData(1_048_577UL, 2L)]
    [InlineData(10_737_418_240UL, 10_240L)]
    public void Bytes_are_rounded_up_to_whole_mebibytes(ulong bytes, long expected)
    {
        Assert.Equal(expected, MailboxMapper.MibOf(bytes));
    }

    /// <summary>Each status is named by its own display key, and an unknown one is refused rather than guessed.</summary>
    [Fact]
    public async Task Each_status_is_named_by_its_own_display_key()
    {
        using var context = new MailTestContext();
        var domain = await context.SeedDomainAsync(Guid.NewGuid(), "example.com");
        var mailbox = await context.SeedMailboxAsync(domain, "info");
        var names = new MailboxGroupDisplayNames();
        var owners = new Dictionary<Guid, string>();

        Assert.Equal(
            "MailboxStatusActive", MailboxMapper.From(mailbox, "example.com", owners, null, MailboxStatus.Active, names).StatusName);
        Assert.Equal(
            "MailboxStatusSuspended", MailboxMapper.From(mailbox, "example.com", owners, null, MailboxStatus.Suspended, names).StatusName);
        Assert.Equal(
            "MailboxStatusDisabled", MailboxMapper.From(mailbox, "example.com", owners, null, MailboxStatus.Disabled, names).StatusName);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            return MailboxMapper.From(mailbox, "example.com", owners, null, (MailboxStatus)99, names);
        });
    }

    /// <summary>The owner's stored switch travels beside a status that hides it, so a suspended mailbox still reads as switched on.</summary>
    [Fact]
    public async Task The_stored_switch_is_carried_beside_a_status_that_hides_it()
    {
        using var context = new MailTestContext();
        var domain = await context.SeedDomainAsync(Guid.NewGuid(), "example.com");
        var mailbox = await context.SeedMailboxAsync(domain, "info");
        var names = new MailboxGroupDisplayNames();
        var owners = new Dictionary<Guid, string>();

        var suspendedButOn = MailboxMapper.From(mailbox, "example.com", owners, null, MailboxStatus.Suspended, names);
        mailbox.Disable();
        var off = MailboxMapper.From(mailbox, "example.com", owners, null, MailboxStatus.Disabled, names);

        Assert.True(suspendedButOn.Enabled);
        Assert.False(off.Enabled);
    }

    /// <summary>A switched-off mailbox is sent to the agent as disabled, and a null hash is sent as "keep the current one".</summary>
    [Fact]
    public async Task A_switched_off_mailbox_is_sent_as_disabled_and_a_null_hash_stays_null()
    {
        using var context = new MailTestContext();
        var domain = await context.SeedDomainAsync(Guid.NewGuid(), "example.com");
        var mailbox = await context.SeedMailboxAsync(domain, "info", 256);
        mailbox.Disable();

        var arguments = MailboxAgentMapper.Current("acme", "example.com", mailbox, null);

        Assert.True(arguments.Disabled);
        Assert.Null(arguments.PasswordHash);
        Assert.Equal(256u, arguments.QuotaMib);
        Assert.Equal("acme", arguments.AccountUsername);
        Assert.Equal("example.com", arguments.Domain);
        Assert.Equal("info", arguments.LocalPart);
    }
}
