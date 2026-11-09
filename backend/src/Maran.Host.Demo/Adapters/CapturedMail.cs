namespace Maran.Host.Demo.Adapters;

/// <summary>What the demo remembers of a message it did not send: who, what about and when, never the body.</summary>
/// <param name="Recipient">The address the message was for, capped.</param>
/// <param name="Subject">The subject line, capped.</param>
/// <param name="At">When the panel handed it over.</param>
public sealed record CapturedMail(string Recipient, string Subject, DateTimeOffset At);
