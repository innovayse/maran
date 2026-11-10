namespace Maran.Sdk.Contracts;

/// <summary>
/// The panel's verdict on a password an administrator chose for someone else's first sign-in. It names
/// the one rule that failed, so the person who typed the password is told which one to fix instead of
/// being handed a sentence about a different part of the form.
/// </summary>
public enum InitialPasswordVerdict
{
    /// <summary>The password meets every rule the panel applies to a password a person sets.</summary>
    Accepted,

    /// <summary>The password is shorter than the minimum length the operator's security policy names.</summary>
    TooShort,

    /// <summary>The password is longer than the panel hashes.</summary>
    TooLong,

    /// <summary>The password equals the login's own name.</summary>
    SameAsUsername,
}
