using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.ValueObjects;

namespace Maran.Modules.Identity.Models;

/// <summary>
/// Everything a completed sign-in produces: the response body, and — separately — the session whose
/// refresh token the controller must put in a cookie.
/// </summary>
/// <remarks>
/// <para>
/// The two are separate fields rather than one object because they travel to different places. The
/// handler must not know about cookies (it has no <c>HttpContext</c>, and a Wolverine handler may
/// run without a request at all), and the response body must never carry the refresh token. Keeping
/// them apart in the type makes putting the token in the body an act of writing new code rather
/// than an oversight.
/// </para>
/// <para>
/// <b>Both fields are required, and nothing can hold one without the other.</b> Verifying a second
/// factor and rotating a refresh cookie can only sign somebody in, so both operations return this
/// directly. Login has a second answer, and it expresses it by having no <c>AuthenticatedOutcome</c>
/// at all rather than by nulling a field inside one; see <see cref="LoginOutcome"/>.
/// </para>
/// </remarks>
/// <param name="AccessToken">The signed token the caller receives, with its expiry and enrolment gate.</param>
/// <param name="User">Who signed in, as the panel knows them — never a wire shape.</param>
/// <param name="Session">The issued session, whose refresh token becomes the cookie.</param>
public sealed record AuthenticatedOutcome(AccessToken AccessToken, User User, IssuedSession Session);
