using Maran.Modules.Accounts.Domain.Enums;

namespace Maran.Modules.Accounts.Common;

/// <summary>One state a hosting account can be in, as the accounts screens offer and print it.</summary>
/// <param name="Status">
/// The state itself: the enum, so the panel's one camelCase enum converter spells it exactly as the
/// list's <c>status</c> filter binds it and as an account's own <c>status</c> field reads, and the
/// filter sends back what it was given.
/// </param>
/// <param name="StatusName">The state as an operator or customer reads it, resolved for the request's culture.</param>
public sealed record AccountStatusChoiceDto(AccountStatus Status, string StatusName);
