namespace Maran.Host.Demo.Common;

/// <summary>What the public demonstration tells every visitor about itself.</summary>
/// <param name="IntervalHours">Hours between resets.</param>
/// <param name="LastResetAt">When the demonstration last reset, or null until one was recorded.</param>
/// <param name="NextResetAt">When it resets next, or null until a reset was recorded.</param>
/// <param name="Identities">The published sign-ins.</param>
/// <param name="RefusalReasons">Why the controls the demo turns off are turned off, in the caller's language.</param>
/// <param name="AbuseContact">Where to report abusive content: an e-mail address or an https URL.</param>
public sealed record DemoStatusDto(
    int IntervalHours,
    DateTimeOffset? LastResetAt,
    DateTimeOffset? NextResetAt,
    IReadOnlyList<DemoIdentityDto> Identities,
    IReadOnlyList<DemoRefusalReasonDto> RefusalReasons,
    string AbuseContact);
