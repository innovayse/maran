using Maran.Modules.Identity.Domain.Entities;

namespace Maran.Modules.Identity.Models;

/// <summary>
/// The login that owns a hosting account together with the membership that makes it so. Both are
/// tracked by the module's context, so a caller can change either and save.
/// </summary>
/// <param name="Login">The owning login.</param>
/// <param name="Membership">The owner membership of that login for the account; carries the account's suspension state.</param>
public sealed record AccountOwner(User Login, AccountMembership Membership);
