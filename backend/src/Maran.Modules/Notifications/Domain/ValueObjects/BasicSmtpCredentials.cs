using System.Text;

namespace Maran.Modules.Notifications.Domain.ValueObjects;

/// <summary>The mail server wants a user name and password, and the panel authenticates with them.</summary>
/// <param name="Username">The submission user name; never empty.</param>
/// <param name="Password">
/// The submission password in plain text, decrypted on load. It is held in memory because sending is
/// impossible without it; it is never logged, never journalled, and never returned by a query.
/// </param>
public sealed record BasicSmtpCredentials(string Username, string Password) : SmtpCredentials
{
    /// <summary>Prints the user name only, so a logged or formatted value never carries the password.</summary>
    /// <param name="builder">The builder the generated <c>ToString</c> writes into.</param>
    /// <returns><c>true</c>, because a member was written.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Username = ").Append(Username);

        return true;
    }
}
