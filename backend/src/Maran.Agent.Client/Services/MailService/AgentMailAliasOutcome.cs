namespace Maran.Agent.Client.Services.MailService;

/// <summary>What the replacing of a domain's aliases left on the host.</summary>
/// <param name="AliasCount">How many aliases the domain has now.</param>
/// <param name="CatchAllSet">Whether the domain has a catch-all now.</param>
public sealed record AgentMailAliasOutcome(uint AliasCount, bool CatchAllSet);
