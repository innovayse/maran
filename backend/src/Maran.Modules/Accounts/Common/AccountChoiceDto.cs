namespace Maran.Modules.Accounts.Common;

/// <summary>
/// One entry of the account picker: just enough to name an account and to send its id back.
/// </summary>
/// <param name="Id">The account's identity.</param>
/// <param name="Name">The account's unique short name.</param>
public sealed record AccountChoiceDto(Guid Id, string Name);
