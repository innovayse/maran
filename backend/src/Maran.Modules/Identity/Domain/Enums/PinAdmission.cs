namespace Maran.Modules.Identity.Domain.Enums;

/// <summary>Whether a user may pin one more page, and if not, why not.</summary>
public enum PinAdmission
{
    /// <summary>The page may be pinned.</summary>
    Admitted,

    /// <summary>The user already pinned this page.</summary>
    AlreadyPinned,

    /// <summary>The user already holds as many pins as the panel allows.</summary>
    LimitReached,
}
