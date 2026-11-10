namespace Maran.Modules.Ssl.Common;

/// <summary>
/// What the panel can issue for one of the caller's domains, so the screen offers only what can work.
/// </summary>
/// <remarks>
/// Built by the backend because only the backend knows whether this server serves the domain's DNS zone, and
/// the SPA keeps no domain constants (rules/vue.md "Data comes from the backend").
/// </remarks>
/// <param name="Domain">The domain the answer is about.</param>
/// <param name="WildcardAvailable">Whether a wildcard certificate can be requested for the domain now.</param>
/// <param name="WildcardUnavailableCode">
/// The machine-stable code of what is in the way, or null when a wildcard can be requested. Read for
/// behaviour only, never for text.
/// </param>
/// <param name="WildcardUnavailableMessage">
/// That code as a sentence in the caller's language, or null when a wildcard can be requested.
/// </param>
public sealed record IssuanceOptionsDto(
    string Domain,
    bool WildcardAvailable,
    string? WildcardUnavailableCode,
    string? WildcardUnavailableMessage);
