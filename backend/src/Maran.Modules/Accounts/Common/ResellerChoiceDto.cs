namespace Maran.Modules.Accounts.Common;

/// <summary>
/// One entry of the reseller picker: just enough to name a reseller and to send its id back, as the
/// owner select of the account form does.
/// </summary>
/// <param name="Id">The reseller's identity.</param>
/// <param name="Name">The reseller's unique short name.</param>
public sealed record ResellerChoiceDto(Guid Id, string Name);
