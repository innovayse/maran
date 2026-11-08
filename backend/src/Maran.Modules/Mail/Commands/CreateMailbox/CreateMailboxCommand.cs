using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Mail.Commands.CreateMailbox;

/// <summary>
/// Creates a mailbox in one of the caller's mail domains, with the password its owner chose.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Password"/> is a plain string because the command binds from JSON, which is the one thing a
/// <c>SensitiveString</c> cannot do. That makes the command itself the thing to keep away from every
/// sink: it is never passed to a logger, never journaled, never echoed in an answer, and the record's
/// generated <c>ToString</c> is replaced so that printing it cannot reveal the password either. What
/// leaves the handler is the hash (<c>MailPasswordHashing</c>), wrapped in <c>SensitiveString</c> on its
/// way to the agent.
/// </para>
/// <para>
/// The command names no account. The owner is the domain's own account, read through the tenant-filtered
/// context, so a customer cannot create a mailbox in a domain somebody else owns, and an administrator
/// cannot create one under an account other than the domain's.
/// </para>
/// </remarks>
/// <param name="MailDomainId">The mail domain the mailbox lives in.</param>
/// <param name="LocalPart">The part of the address before the <c>@</c>; trimmed and lower-cased before use.</param>
/// <param name="Password">The password the owner chose. Never logged, journaled or returned.</param>
/// <param name="QuotaMib">The size limit, in MiB.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal. Established by the server.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal. Established by the server.</param>
public sealed record CreateMailboxCommand(
    Guid MailDomainId,
    string LocalPart,
    string Password,
    int QuotaMib,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "")
{
    /// <summary>Prints the command without its password, so no sink that formats the command can leak it.</summary>
    /// <returns>The identifying members and a mask where the password would be.</returns>
    public override string ToString()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{nameof(CreateMailboxCommand)} {{ MailDomainId = {MailDomainId}, LocalPart = {LocalPart}, "
            + $"Password = [redacted], QuotaMib = {QuotaMib} }}");
    }
}
