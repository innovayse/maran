namespace Maran.Agent.Client.Services.MailService;

/// <summary>
/// One recipient address that expands to other hosts and the number of deliveries outside the host one message to it
/// makes: the agent charges the sender's hourly limit that many times.
/// </summary>
/// <param name="Alias">The recipient: an alias address (<c>local@domain</c>) or a domain's catch-all (<c>@domain</c>), lower-case, on a domain of the account.</param>
/// <param name="ExternalTargets">How many deliveries outside the host the whole expansion makes, at most the MTA's expansion limit; zero for an alias that stays inside the account.</param>
public sealed record AgentAliasFanout(string Alias, uint ExternalTargets);
