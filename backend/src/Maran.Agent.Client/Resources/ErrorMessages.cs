namespace Maran.Agent.Client.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/ErrorMessages.resx</c> (+ <c>.ru</c>/<c>.hy</c>), the
/// convention every resource family in this product follows (rules/csharp.md "Resources are reached
/// through <c>IStringLocalizer&lt;T&gt;</c>"): the marker makes the file visible to a reader of the
/// folder and is the type a typed <c>IStringLocalizer&lt;T&gt;</c> would key on if this project ever
/// needed one. Its keys are looked up today by code through the panel-wide
/// <c>IErrorTextProvider</c>, which is what an agent failure travelling as an
/// <c>Error.Code</c> reaches. Carries the customer-facing text for the
/// agent failures this project surfaces as error codes: <c>AgentUnspecified</c>,
/// <c>AgentInvalidInput</c>, <c>AgentAlreadyExists</c>, <c>AgentNotFound</c>,
/// <c>AgentValidationFailed</c>, <c>AgentSystemFailure</c> and <c>AgentInvalidResponse</c> — each
/// key equal to the code produced by
/// <see cref="Services.SystemService.AgentSystemClient"/> exactly, so there is one identifier
/// rather than a code plus a separate resource key that can drift apart.
/// </summary>
/// <remarks>
/// The wording deliberately describes the outcome and the customer's next step only. The agent is
/// the sole root process on the machine, so its internals — unit names, paths, tool output,
/// validation internals — never appear in text a customer reads (rules/security.md "Secrets").
/// The operator's diagnosis comes from the logged <c>Error.Message</c> and the correlation id.
/// </remarks>
public sealed class ErrorMessages
{
}
