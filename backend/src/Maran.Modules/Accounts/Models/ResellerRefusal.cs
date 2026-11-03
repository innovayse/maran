namespace Maran.Modules.Accounts.Models;

/// <summary>
/// Why the reseller rules refused a write: the typed failure to answer with, and the offending
/// figures when the refusal has some to publish.
/// </summary>
/// <remarks>
/// A carrier between <see cref="Services.ResellerAllocationGuard"/> and the handlers that ask it, never
/// put on the wire itself. <see cref="Error"/> carries a code and nothing else (rules/csharp.md), so a
/// refusal that must name the figures of a plan that broke the envelope carries them beside it, in
/// the panel's one convention for failure facts.
/// </remarks>
/// <param name="Error">The typed failure, code and kind together.</param>
/// <param name="Extension">
/// The typed facts the problem response publishes beside the code, or <c>null</c> when the refusal
/// has none (an exceeded count names nothing the caller does not already know).
/// </param>
public sealed record ResellerRefusal(Error Error, ProblemExtension? Extension = null);
