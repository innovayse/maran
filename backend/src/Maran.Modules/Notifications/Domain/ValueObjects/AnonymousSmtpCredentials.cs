namespace Maran.Modules.Notifications.Domain.ValueObjects;

/// <summary>The mail server takes no credentials; the panel never authenticates.</summary>
public sealed record AnonymousSmtpCredentials : SmtpCredentials;
