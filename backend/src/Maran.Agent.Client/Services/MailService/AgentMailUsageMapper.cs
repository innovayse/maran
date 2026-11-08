using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.MailService;

/// <summary>Reads the wire's mailbox usage into the panel's own.</summary>
internal static class AgentMailUsageMapper
{
    /// <summary>Projects one wire mailbox usage onto the panel's own.</summary>
    /// <param name="wire">The wire entry.</param>
    /// <returns>The entry; a mailbox the agent could not measure keeps a <c>null</c> reading and never zeros.</returns>
    internal static AgentMailboxUsage ToUsage(MailboxUsage wire)
    {
        var reading = wire.Reading is null
            ? null
            : new AgentMailboxUsageReading(wire.Reading.BytesUsed, wire.Reading.MessageCount);

        return new AgentMailboxUsage(wire.Domain, wire.LocalPart, reading);
    }
}
