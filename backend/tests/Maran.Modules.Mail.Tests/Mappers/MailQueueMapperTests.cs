using Maran.Agent.Client.Services.MailService;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Mappers;
using Maran.Modules.Mail.Resources;
using Maran.Modules.Mail.Tests.TestSupport;

namespace Maran.Modules.Mail.Tests.Mappers;

/// <summary>Behaviour of the mapper that restates the agent's queue listing for the wire.</summary>
public sealed class MailQueueMapperTests
{
    /// <summary>Every agent queue maps to its own queue, the whole vocabulary.</summary>
    /// <param name="queue">The agent's value.</param>
    /// <param name="expected">This module's value.</param>
    [Theory]
    [InlineData(AgentMailQueueName.Active, MailQueueName.Active)]
    [InlineData(AgentMailQueueName.Deferred, MailQueueName.Deferred)]
    [InlineData(AgentMailQueueName.Held, MailQueueName.Held)]
    [InlineData(AgentMailQueueName.Incoming, MailQueueName.Incoming)]
    public void Every_agent_queue_maps_to_its_own_queue(AgentMailQueueName queue, MailQueueName expected)
    {
        var entries = MailQueueMapper.ToEntries(
            Listing(queue, AgentMailDeferralClass.None, "a@example.com"),
            new AliasKeyLocalizer<DisplayNames>());

        Assert.Equal(expected, Assert.Single(entries).Queue);
    }

    /// <summary>Every agent deferral class maps to its own class, the whole vocabulary.</summary>
    /// <param name="deferral">The agent's value.</param>
    /// <param name="expected">This module's value.</param>
    [Theory]
    [InlineData(AgentMailDeferralClass.None, MailDeferralClass.None)]
    [InlineData(AgentMailDeferralClass.Network, MailDeferralClass.Network)]
    [InlineData(AgentMailDeferralClass.RemoteRefused, MailDeferralClass.RemoteRefused)]
    [InlineData(AgentMailDeferralClass.LocalError, MailDeferralClass.LocalError)]
    [InlineData(AgentMailDeferralClass.Other, MailDeferralClass.Other)]
    public void Every_agent_deferral_class_maps_to_its_own_class(
        AgentMailDeferralClass deferral,
        MailDeferralClass expected)
    {
        var entries = MailQueueMapper.ToEntries(
            Listing(AgentMailQueueName.Deferred, deferral, "a@example.com"),
            new AliasKeyLocalizer<DisplayNames>());

        Assert.Equal(expected, Assert.Single(entries).DeferralClass);
    }

    /// <summary>Every queue and every deferral class is named by its own display-name key, the whole vocabulary.</summary>
    /// <param name="queue">The agent's queue.</param>
    /// <param name="queueKey">The key that must name it.</param>
    /// <param name="deferral">The agent's deferral class.</param>
    /// <param name="deferralKey">The key that must name it.</param>
    [Theory]
    [InlineData(AgentMailQueueName.Active, "MailQueueNameActive", AgentMailDeferralClass.None, "MailDeferralNone")]
    [InlineData(AgentMailQueueName.Deferred, "MailQueueNameDeferred", AgentMailDeferralClass.Network, "MailDeferralNetwork")]
    [InlineData(AgentMailQueueName.Held, "MailQueueNameHeld", AgentMailDeferralClass.RemoteRefused, "MailDeferralRemoteRefused")]
    [InlineData(AgentMailQueueName.Incoming, "MailQueueNameIncoming", AgentMailDeferralClass.LocalError, "MailDeferralLocalError")]
    [InlineData(AgentMailQueueName.Active, "MailQueueNameActive", AgentMailDeferralClass.Other, "MailDeferralOther")]
    public void Every_queue_and_deferral_class_is_named_by_its_own_display_name_key(
        AgentMailQueueName queue,
        string queueKey,
        AgentMailDeferralClass deferral,
        string deferralKey)
    {
        var entry = Assert.Single(MailQueueMapper.ToEntries(
            Listing(queue, deferral, "a@example.com"),
            new AliasKeyLocalizer<DisplayNames>()));

        Assert.Equal(queueKey, entry.QueueDisplayName);
        Assert.Equal(deferralKey, entry.DeferralDisplayName);
    }

    /// <summary>An untrusted sender is carried through as data, unparsed and unchanged.</summary>
    [Fact]
    public void An_untrusted_sender_is_carried_through_unchanged()
    {
        const string sender = "<script>alert(1)</script>@example.net";

        var entries = MailQueueMapper.ToEntries(
            Listing(AgentMailQueueName.Active, AgentMailDeferralClass.None, sender),
            new AliasKeyLocalizer<DisplayNames>());

        Assert.Equal(sender, Assert.Single(entries).Sender);
    }

    /// <summary>Builds a one-entry listing.</summary>
    /// <param name="queue">The queue the entry waits in.</param>
    /// <param name="deferral">The deferral class.</param>
    /// <param name="sender">The sender.</param>
    /// <returns>The listing.</returns>
    private static AgentMailQueueListing Listing(AgentMailQueueName queue, AgentMailDeferralClass deferral, string sender)
    {
        return new AgentMailQueueListing(
            [new AgentMailQueueEntry("4A1B2C3D4E5F", queue, sender, 1, 100, DateTimeOffset.UnixEpoch, deferral)],
            1,
            false);
    }
}
