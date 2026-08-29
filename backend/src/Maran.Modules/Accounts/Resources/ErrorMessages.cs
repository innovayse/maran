namespace Maran.Modules.Accounts.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/ErrorMessages.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources
/// are reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries the Accounts module's domain
/// failures surfaced as error codes: <c>AccountNotFound</c>, <c>AccountNameTaken</c>,
/// <c>AccountDomainTaken</c>, <c>PlanNotFound</c> — each key equal to the matching
/// <see cref="Errors.AccountsErrors"/> machine code exactly, so there is one identifier rather than
/// a code plus a separate resource key that can drift apart.
/// </summary>
public sealed class ErrorMessages
{
}
