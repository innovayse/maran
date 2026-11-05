namespace Maran.Modules.Sftp.Domain.Policies;

/// <summary>
/// The size limits on the public keys a login may hold: how many, and how long a human label may be.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MaxPerLogin"/> is the agent's own ceiling, not a second policy chosen here: the agent
/// refuses a whole key set larger than <c>AuthorizedKeySet::MAX_KEYS</c> (32) with
/// <c>INVALID_INPUT</c> and writes nothing. A panel limit above it would let a customer store a key
/// that can never become live; one below it would be a silent second policy. The two numbers are
/// held together by a test, and a change to either moves both.
/// </para>
/// <para>
/// A stateless rule over values, so <c>Domain/Policies/</c> (rules/csharp.md).
/// </para>
/// </remarks>
public static class SftpKeyLimits
{
    /// <summary>The most keys one login may hold, equal to the agent's limit.</summary>
    public const int MaxPerLogin = 32;

    /// <summary>The longest label a customer may give a key, in characters.</summary>
    public const int MaxLabelLength = 64;
}
