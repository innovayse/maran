namespace Maran.Host.Security;

/// <summary>
/// The <see cref="ICurrentUser"/> in force until authentication ships. It reports an anonymous,
/// non-administrator principal with no owning account, so controllers can be constructed and the
/// panel runs end to end during development.
/// </summary>
/// <remarks>
/// This is deliberately the LEAST privileged answer, not the most convenient one. A stub that
/// claimed <see cref="IsAdmin"/> would let every unauthenticated request through the moment the
/// first authorization check is written, and the mistake would look like working software. With
/// this implementation, any code that starts checking permissions denies by default until real
/// authentication replaces it — the failure mode is a refusal, not a silent grant.
/// </remarks>
public sealed class UnauthenticatedCurrentUser : ICurrentUser
{
    /// <inheritdoc />
    public Guid UserId => Guid.Empty;

    /// <inheritdoc />
    public Guid? AccountId => null;

    /// <inheritdoc />
    public bool IsAdmin => false;
}
