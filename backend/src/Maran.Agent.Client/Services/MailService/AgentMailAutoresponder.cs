namespace Maran.Agent.Client.Services.MailService;

/// <summary>An out-of-office reply.</summary>
/// <param name="Subject">The reply's subject: one line, at most 200 characters, no control characters, no double quote and no backslash.</param>
/// <param name="Body">The reply's text: at most 4096 bytes, lines separated by a line feed.</param>
/// <param name="StartsAt">The first instant the reply is active; <c>null</c> means from now.</param>
/// <param name="EndsAt">The last instant the reply is active; <c>null</c> means until cleared. When set it must be after <paramref name="StartsAt"/>.</param>
/// <param name="ReplyIntervalDays">Days before the same sender is answered again, 1 to 30; <c>null</c> means the agent's default of one day.</param>
public sealed record AgentMailAutoresponder(
    string Subject,
    string Body,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    uint? ReplyIntervalDays);
