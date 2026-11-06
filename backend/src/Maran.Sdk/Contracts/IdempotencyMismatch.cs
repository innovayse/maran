namespace Maran.Sdk.Contracts;

/// <summary>
/// The key was used before by a DIFFERENT request (another method, route, parameters or body): the
/// caller reused a key for new work, which is refused rather than answered with the old result.
/// </summary>
public sealed record IdempotencyMismatch : IdempotencyBegin;
