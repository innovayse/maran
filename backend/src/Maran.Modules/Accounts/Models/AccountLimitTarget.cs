using Maran.Modules.Accounts.Domain.ValueObjects;

namespace Maran.Modules.Accounts.Models;

/// <summary>
/// One account a plan's figures are about to be put on, with what that account holds in place of the
/// plan's: the pair the host-side enforcers need to push <c>override ?? plan</c> and not the plan alone.
/// </summary>
/// <param name="Name">The account's system user name.</param>
/// <param name="Overrides">The ceilings the account states for itself; empty when it follows its plan.</param>
public sealed record AccountLimitTarget(string Name, AccountLimitOverrides Overrides);
