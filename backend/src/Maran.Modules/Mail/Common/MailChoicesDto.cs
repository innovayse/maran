namespace Maran.Modules.Mail.Common;

/// <summary>The reference data the mail screens select from and validate against.</summary>
/// <param name="DomainStatuses">The statuses a mail domain can be read as.</param>
/// <param name="MailboxStatuses">The statuses a mailbox can be read as.</param>
/// <param name="AliasKinds">The kinds of alias.</param>
/// <param name="Limits">The numeric bounds for the caller.</param>
public sealed record MailChoicesDto(
    IReadOnlyList<MailChoiceDto> DomainStatuses,
    IReadOnlyList<MailChoiceDto> MailboxStatuses,
    IReadOnlyList<MailChoiceDto> AliasKinds,
    MailFormLimitsDto Limits);
