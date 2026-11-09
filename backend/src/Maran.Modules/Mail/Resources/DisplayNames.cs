namespace Maran.Modules.Mail.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/DisplayNames.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources are
/// reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries every user-facing name the Mail module owns:
/// <c>MailModuleDisplayName</c>, the labels of its menu entries (<c>MailMenu*</c>) and route titles
/// (<c>MailRoute*</c>), and the names of its closed vocabularies — the states of a domain and a mailbox
/// (<c>MailDomainStatus*</c>, <c>MailboxStatus*</c>), the kinds of alias (<c>MailAliasKind*</c>) and what a
/// DNS record is for (<c>MailDnsRecordPurpose*</c>).
/// </summary>
public sealed class DisplayNames
{
}
