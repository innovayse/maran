using Maran.Agent.Client.Services.MailService;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Mappers;

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
        var dto = MailQueueMapper.ToDto(Listing(queue, AgentMailDeferralClass.None, "a@example.com"));

        Assert.Equal(expected, Assert.Single(dto.Entries).Queue);
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
        var dto = MailQueueMapper.ToDto(Listing(AgentMailQueueName.Deferred, deferral, "a@example.com"));

        Assert.Equal(expected, Assert.Single(dto.Entries).DeferralClass);
    }

    /// <summary>An untrusted sender is carried through as data, unparsed and unchanged.</summary>
    [Fact]
    public void An_untrusted_sender_is_carried_through_unchanged()
    {
        const string sender = "<script>alert(1)</script>@example.net";

        var dto = MailQueueMapper.ToDto(Listing(AgentMailQueueName.Active, AgentMailDeferralClass.None, sender));

        Assert.Equal(sender, Assert.Single(dto.Entries).Sender);
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
