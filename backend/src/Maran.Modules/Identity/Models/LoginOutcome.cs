
namespace Maran.Modules.Identity.Models;

/// <summary>
/// What the login handler produces: a completed sign-in, or nothing because a second factor is owed.
/// </summary>
/// <remarks>
/// <para>
/// <b>The body and the cookie travel together or not at all.</b> A sign-in produces two things that
/// go to different places — a response body for the caller and a session whose refresh token only
/// the controller may turn into a cookie — and this used to hold them as two loose fields, one of
/// them nullable. That admitted a body announcing a signed-in user beside a null session: a caller
/// told they were in while the refresh cookie was silently never set, and a session that ends
/// fifteen minutes later for no visible reason. Nesting them in
/// <see cref="AuthenticatedOutcome"/> and making the WHOLE PAIR the nullable field is what removes
/// that state, with no constructor guard to trust and none to forget.
/// </para>
/// <para>
/// The handler still may not know about cookies — it has no <c>HttpContext</c>, and a Wolverine
/// handler may run without a request at all — and the response body still never carries the refresh
/// token. Both facts survive; only the pairing changed.
/// </para>
/// </remarks>
/// <param name="Authenticated">
/// The completed sign-in, or <c>null</c> when the password was right and a second factor is
/// required — in which case there is deliberately no session and no token, because issuing anything
/// then would make the factor optional for whoever holds the password.
/// </param>
public sealed record LoginOutcome(AuthenticatedOutcome? Authenticated);
