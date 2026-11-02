namespace Maran.Modules.Notifications.Domain.ValueObjects;

/// <summary>
/// How the panel presents itself to the mail server: with no credentials at all, or with a user name
/// and password. Exactly one of two cases, each its own type, so "no credentials" is never an empty
/// user name that a caller has to know to test for.
/// </summary>
/// <remarks>
/// A relay on localhost takes no credentials, and offering some it did not ask for is how a working
/// relay starts refusing the panel's mail — so the anonymous case is a first-class configuration and
/// not a missing value. The stored columns still spell it as an empty user name; that spelling is
/// read once, in <see cref="Entities.SmtpSettings.Credentials"/>, and nowhere else. The constructor is
/// private to this assembly, so <see cref="AnonymousSmtpCredentials"/> and
/// <see cref="BasicSmtpCredentials"/> are the whole set.
/// </remarks>
public abstract record SmtpCredentials
{
    /// <summary>Closes the hierarchy to the two cases this module declares.</summary>
    private protected SmtpCredentials()
    {
    }
}
