namespace Maran.Agent.Client.Services.TransferService;

/// <summary>Something the read could not turn into a clean inventory entry.</summary>
/// <param name="Kind">What kind of thing it concerns.</param>
/// <param name="Subject">
/// The subject, validated and printable (an account or domain name), or empty when it concerns the read as
/// a whole. The agent never echoes unvalidated text here.
/// </param>
public sealed record AgentSourceWarning(SourceWarningKind Kind, string Subject);
