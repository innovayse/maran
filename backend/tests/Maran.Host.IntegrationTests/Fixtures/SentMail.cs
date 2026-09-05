namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>One message a test's mailer was asked to send.</summary>
/// <param name="Recipient">Who it was addressed to.</param>
/// <param name="Subject">The subject line.</param>
/// <param name="Body">The body, which for a reset carries the live token.</param>
public sealed record SentMail(string Recipient, string Subject, string Body);
