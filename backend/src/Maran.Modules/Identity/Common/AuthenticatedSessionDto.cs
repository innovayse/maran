namespace Maran.Modules.Identity.Common;

/// <summary>
/// The body of every answer in which the panel has actually signed somebody in: the access token,
/// when it dies, who it belongs to, and whether it is confined to two-factor enrolment.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every field is required, and that is the whole point of the type.</b> The shape it replaced
/// made all four optional beside two booleans, so "a token with no expiry", "an expiry with no
/// token" and "signed in as nobody" were all things a producer could write and a consumer had to
/// defend against. None of them is a state this panel can be in, and none of them can now be
/// spelled: a caller holding one of these holds all four facts or does not hold the object.
/// </para>
/// <para>
/// <b>It carries no "a second factor is owed" field</b>, because the answer that says so carries
/// none of this — see <see cref="LoginResultDto"/>. Refresh and two-factor verification cannot
/// produce that answer at all, so their bodies are this type directly and there is no field on them
/// for a consumer to test.
/// </para>
/// <para>
/// Deliberately no refresh token, like the login body it is part of: that token goes to an httpOnly
/// cookie the page's JavaScript can never read, and a copy in the JSON body would undo exactly the
/// protection the cookie exists for (spec §10).
/// </para>
/// </remarks>
/// <param name="AccessToken">The signed access token, compact-serialized.</param>
/// <param name="ExpiresAt">When it stops being accepted, so the SPA can refresh before a call fails.</param>
/// <param name="User">Who the token signs in.</param>
/// <param name="RequiresTwoFactorSetup">
/// True when the panel forces administrators to hold a second factor and this one does not yet. The
/// sign-in SUCCEEDED — the token above is real — but it reaches only the enrolment endpoints, and
/// every other one answers 403 until enrolment is finished. That refusal is enforced server-side by
/// <c>TwoFactorEnrolmentCompleteHandler</c> reading the claim inside the token; this field only
/// tells the SPA which screen to render, and a browser that ignores it gains nothing.
/// It is copied from the issued token rather than recomputed — see
/// <see cref="Mappers.AuthenticatedSessionMapper"/>, which is the only thing that builds this.
/// </param>
public sealed record AuthenticatedSessionDto(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    AuthenticatedUserDto User,
    bool RequiresTwoFactorSetup);
