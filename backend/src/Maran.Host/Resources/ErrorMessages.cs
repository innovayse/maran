namespace Maran.Host.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/ErrorMessages.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources
/// are reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries the failures the composition
/// root itself produces, which belong to no module: <c>HostUnexpectedError</c> and
/// <c>HostValidationFailed</c>, written by <see cref="Middleware.ExceptionMiddleware"/>;
/// <c>HostRateLimited</c>, written by the rejection handler in
/// <see cref="Extensions.RateLimitingExtensions"/>; and <c>HostForbidden</c> and
/// <c>HostTwoFactorSetupRequired</c>, written for a policy refusal by
/// <see cref="Authorization.ProblemDetailsAuthorizationResultWriter"/>. Each key equals the machine code placed in the
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
