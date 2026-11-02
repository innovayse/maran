namespace Maran.Modules.Identity.Common;

/// <summary>One hosting account the signed-in person may switch to, as the header's selector lists it.</summary>
/// <param name="AccountId">The account's identity, sent back to switch to it.</param>
/// <param name="Name">The account's name, shown as the selector's label.</param>
/// <param name="IsCurrent">Whether this is the account the current sign-in is acting for (not the account lifecycle state).</param>
public sealed record SwitchableAccountDto(Guid AccountId, string Name, bool IsCurrent);
