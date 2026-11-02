namespace Maran.Modules.Identity.Common;

/// <summary>The answer to switching accounts: the token that now speaks for the chosen one.</summary>
/// <remarks>
/// Carries no refresh token and sets no cookie: the session is the same one, only its active account
/// changed, so the refresh cookie the browser already holds stays valid.
/// </remarks>
/// <param name="AccessToken">The replacement access token, whose account claim is the chosen account.</param>
/// <param name="ExpiresAt">When it stops being accepted, so the SPA can refresh before a call fails.</param>
/// <param name="AccountId">The account the session now acts for.</param>
public sealed record ActiveAccountDto(string AccessToken, DateTimeOffset ExpiresAt, Guid AccountId);
