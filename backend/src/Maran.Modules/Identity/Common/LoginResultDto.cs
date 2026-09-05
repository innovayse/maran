namespace Maran.Modules.Identity.Common;

/// <summary>
/// The body of a login response: the session the caller was given, or nothing at all because a
/// second factor is still owed.
/// </summary>
/// <remarks>
/// <para>
/// <b>One field, two states, and no state that contradicts itself.</b> The shape this replaced
/// carried two independent booleans beside three nullable fields, so it could spell answers this
/// panel has never been able to give: both booleans true at once, an access token with no expiry, a
/// factor owed AND a token issued. Nothing forbade them but prose — a paragraph explaining that one
/// boolean "means the opposite" of the other, which is documentation paying for a shape that does
/// not say it. The SPA paid too: it declared one flag optional and coerced it with <c>=== true</c>,
/// because a contract it could not pin down is one it has to hedge against.
/// </para>
/// <para>
/// <b>Why the discriminant is the payload's presence and not a <c>Kind</c> enum beside it.</b> A tag
/// field next to a nullable payload only agrees with it if something checks — a guarded constructor,
/// a named factory — and a DTO carries no logic (rules/csharp.md), so nothing here could do the
/// checking. The tag would be back to a field a producer can set wrongly and a consumer must
/// distrust, which is the defect being removed. With the payload itself as the discriminant there is
/// no second field to disagree with, and the guarantee holds no matter who constructs the record.
/// The cost, stated: a third login answer would need a new shape rather than a new enum member, and
/// the wire body says which case it is by a null rather than by a word.
/// </para>
/// <para>
/// <b>Only login returns this.</b> Refreshing a session and verifying a second factor cannot owe a
/// factor, so they answer with <see cref="AuthenticatedSessionDto"/> itself — no wrapper, no
/// nullable, and no field about two-factor for their consumers to read.
/// </para>
/// </remarks>
/// <param name="Session">
/// What the caller was signed in as, or <c>null</c> when the password was right and a second factor
/// is required. Null names nobody on purpose: the caller has not finished proving who they are, and
/// a username or role echoed back would be a fact obtained with one factor.
/// </param>
public sealed record LoginResultDto(AuthenticatedSessionDto? Session);
