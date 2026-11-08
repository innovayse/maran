namespace Maran.Modules.Sites.Domain.Enums;

/// <summary>
/// Where a PHP version stands in its upstream support life, as the panel's own table reads it
/// (<c>PhpLifecycle</c>).
/// </summary>
/// <remarks>
/// <see cref="Unknown"/> is a statement of ignorance and never a comfortable one: a version the
/// table has no row for is not reported as supported, because a false assurance about a runtime
/// that no longer gets security fixes is the one wrong answer this type exists to prevent.
/// </remarks>
public enum PhpSupportStatus
{
    /// <summary>The table has no row for the version, so nothing is claimed about it.</summary>
    Unknown = 0,

    /// <summary>The version still receives bug fixes and security fixes.</summary>
    Supported = 1,

    /// <summary>The version receives security fixes only.</summary>
    SecurityOnly = 2,

    /// <summary>The version no longer receives any fix from the PHP project.</summary>
    EndOfLife = 3,
}
