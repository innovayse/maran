namespace Maran.Sdk.Events;

/// <summary>
/// No subscriber reported on the session cull: no module that ends sessions was composed, or none ran.
/// The publisher may then say only that it asked. This is the half <see cref="AccountSuspending"/>'s
/// own remarks are about — a subscriber that does not exist cannot throw, so a completed cascade is
/// not evidence that anything happened.
/// </summary>
public sealed record SessionCullNotReported : SessionCullReading;
