namespace Maran.Modules.Accounts.Queries.ListOwnerLanguages;

/// <summary>
/// Lists every language an account owner's invitation can be written in, so the create-account form
/// offers exactly the set <c>CreateAccountCommandValidator</c> accepts and the SPA holds no list of
/// its own (rules/architecture.md "The backend owns the data, the SPA renders it"). Takes no
/// parameters: the set is the panel's shipped languages, short and fixed.
/// </summary>
public sealed record ListOwnerLanguagesQuery;
