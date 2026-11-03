namespace Maran.Modules.Accounts.Queries.ListAccountStatusChoices;

/// <summary>
/// Lists the states a hosting account can be in, for the accounts list's status filter, its chips
/// and every badge that names a state: reference data the screens select from and print, produced
/// and localized by the panel rather than worded by the SPA (rules/architecture.md "The backend owns
/// the data, the SPA renders it").
/// </summary>
/// <remarks>
/// The whole closed set, in the order the enum declares it, the same for every caller: a status
/// discloses nothing about another account. The accounts list still validates the value it is given
/// against the same enum, so this list is advice and that endpoint is the boundary.
/// </remarks>
public sealed record ListAccountStatusChoicesQuery;
