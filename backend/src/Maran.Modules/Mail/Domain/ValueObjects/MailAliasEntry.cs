namespace Maran.Modules.Mail.Domain.ValueObjects;

/// <summary>
/// One alias, forwarder or catch-all of an account as the mail server reads it: the address it answers to and the
/// destinations it expands into.
/// </summary>
/// <param name="LocalPart">The part before the <c>@</c>, or <c>*</c> for the domain's catch-all.</param>
/// <param name="DomainName">The mail domain the entry is on.</param>
/// <param name="Targets">The normalised destinations, any of which may itself be an alias of the same account.</param>
public sealed record MailAliasEntry(string LocalPart, string DomainName, IReadOnlyList<string> Targets);
