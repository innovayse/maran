namespace Maran.Host.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/ErrorMessages.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources
/// are reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries the failures the composition
/// root itself produces, which belong to no module: <c>HostUnexpectedError</c> and
/// <c>HostValidationFailed</c>, written by <see cref="Middleware.ExceptionMiddleware"/>;
/// <c>HostRateLimited</c>, written by
/// <see cref="RateLimiting.RateLimitedProblemWriter"/> for the rate limiter's rejection and for the dashboard
/// stream's process-wide limit; and <c>HostForbidden</c>,
/// <c>HostPasswordChangeRequired</c>, <c>HostTwoFactorSetupRequired</c> and <c>HostViewAsNotAllowed</c>, written for a policy refusal by
/// <see cref="Authorization.ProblemDetailsAuthorizationResultWriter"/>; and <c>ModuleFrameOutOfScope</c>, written by
/// <see cref="Middleware.ModuleFrameScopeMiddleware"/> for a framed call to an endpoint another module owns. Each key equals the machine code placed in the
/// RFC 7807 payload exactly, so there is one identifier rather than a code plus a separate resource
/// key that can drift apart.
/// </summary>
/// <remarks>
/// Every message is last-resort text shown to whoever made the request, so they carry no exception
/// text, no path and no tool output (rules/security.md "Secrets"). <c>HostUnexpectedError</c> names
/// the correlation id sent alongside it, because that id is the only handle support has for finding
/// the logged cause.
/// </remarks>
public sealed class ErrorMessages
{
}
