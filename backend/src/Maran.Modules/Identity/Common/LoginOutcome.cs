namespace Maran.Modules.Identity.Common;

/// <summary>
/// Everything a successful login produces: the response body, and — separately — the session whose
/// refresh token the controller must put in a cookie.
/// </summary>
/// <remarks>
/// The two are separate fields rather than one object because they travel to different places. The
/// handler must not know about cookies (it has no <c>HttpContext</c>, and a Wolverine handler may
/// run without a request at all), and the response body must never carry the refresh token. Keeping
/// them apart in the type makes putting the token in the body an act of writing new code rather
/// than an oversight.
/// </remarks>
/// <param name="Response">The body returned to the caller.</param>
/// <param name="Session">
/// The issued session, or null when no session was issued — a login awaiting its second factor.
/// </param>
public sealed record LoginOutcome(LoginResultDto Response, IssuedSession? Session);
