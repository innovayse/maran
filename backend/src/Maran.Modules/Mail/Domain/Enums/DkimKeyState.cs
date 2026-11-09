namespace Maran.Modules.Mail.Domain.Enums;

/// <summary>Where a DKIM signing key is in its life.</summary>
public enum DkimKeyState
{
    /// <summary>The key the host signs outgoing mail with right now.</summary>
    Active,

    /// <summary>
    /// A key that was replaced but whose public record is still published, so a message signed just
    /// before the rotation still verifies at its recipient.
    /// </summary>
    Retiring,

    /// <summary>A key whose record may be withdrawn from DNS; it signs nothing and is kept only as history.</summary>
    Retired,
}
