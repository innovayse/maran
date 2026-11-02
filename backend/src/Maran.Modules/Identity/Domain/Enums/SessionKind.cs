namespace Maran.Modules.Identity.Domain.Enums;

/// <summary>Whose sign-in a <see cref="Entities.Session"/> is.</summary>
public enum SessionKind
{
    /// <summary>The user signed in as themselves, with their own credentials.</summary>
    Own,

    /// <summary>
    /// An operator is viewing the panel as this user. The session carries the operator as its actor,
    /// lives for <see cref="Policies.ViewAsLifetime.Duration"/>, and has no refresh token that ever
    /// left the server, so it can never be rotated or outlive that lifetime.
    /// </summary>
    ViewAs,
}
