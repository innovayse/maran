using Maran.Modules.Identity.Domain.Enums;

namespace Maran.Modules.Identity.Domain.ValueObjects;

/// <summary>
/// How a session ended before its expiry: the instant and the reason, which are one fact. A session
/// that was never revoked has no such value (<see cref="Entities.Session.Revocation"/> is null), so
/// there is no instant without a reason and no reason without an instant.
/// </summary>
/// <param name="At">The instant of the revocation.</param>
/// <param name="Reason">Why the session ended; the first reason a session was revoked for survives.</param>
public sealed record SessionRevocation(DateTimeOffset At, SessionRevocationReason Reason);
