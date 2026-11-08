namespace Maran.Modules.Sites.Common;

/// <summary>The customer's PHP settings for their whole account, with the plan's bounds beside each.</summary>
/// <param name="AccountId">The account the settings belong to.</param>
/// <param name="Items">Every setting a customer may change, in screen order, whether or not it has been set.</param>
/// <param name="SitesAffected">How many of the account's PHP sites have a pool the settings reach.</param>
public sealed record PhpSettingsDto(Guid AccountId, IReadOnlyList<PhpSettingItemDto> Items, int SitesAffected);
