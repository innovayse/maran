using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Mail.Commands.ChangeMailboxPassword;

/// <summary>
/// Gives a mailbox a new password, chosen by its owner. Nothing that is already open is ended: a mail
/// client signed in with the old password is refused at its next login, not before.
/// </summary>
/// <remarks>
/// <see cref="Password"/> is a plain string because the command binds from JSON. The command is never
/// passed to a logger, never journaled and never echoed, and the record's generated <c>ToString</c> is
/// replaced so that printing it cannot reveal the password either. Only the hash leaves the handler.
/// </remarks>
/// <param name="Password">The new password. Never logged, journaled or returned.</param>
/// <param name="MailboxId">The mailbox, from the route. Established by the server; never bound from the body.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal. Established by the server.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal. Established by the server.</param>
public sealed record ChangeMailboxPasswordCommand(
    string Password,
    [property: JsonIgnore][property: BindNever][BindNever] Guid MailboxId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "")
{
    /// <summary>Prints the command without its password, so no sink that formats the command can leak it.</summary>
    /// <returns>The mailbox identifier and a mask where the password would be.</returns>
    public override string ToString()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{nameof(ChangeMailboxPasswordCommand)} {{ MailboxId = {MailboxId}, Password = [redacted] }}");
    }
}
