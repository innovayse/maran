using Maran.Agent.Client.Services.MailService;

namespace Maran.Modules.Mail.Models;

/// <summary>
/// Every alias, forwarder and the catch-all of one domain, in the shape the agent replaces them with.
/// </summary>
/// <remarks>
/// The agent's alias call REPLACES the whole domain, so the panel always holds the complete set when it
/// speaks: the set before a change, kept to restore if the row cannot be written, and the set after it.
/// </remarks>
/// <param name="Aliases">Every non-catch-all alias of the domain; empty when there are none.</param>
/// <param name="CatchAllTargets">The catch-all's targets, or <c>null</c> for a domain with no catch-all.</param>
public sealed record MailAliasSet(
    IReadOnlyList<AgentMailAlias> Aliases,
    IReadOnlyList<string>? CatchAllTargets);
