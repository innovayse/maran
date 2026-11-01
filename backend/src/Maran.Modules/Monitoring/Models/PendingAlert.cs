using Maran.Modules.Monitoring.Domain.Enums;

namespace Maran.Modules.Monitoring.Models;

/// <summary>
/// One observation of a round, carried from the step that recorded it against its alert row to the
/// step that announces whatever it changed.
/// </summary>
/// <param name="Kind">Which kind of condition was observed.</param>
/// <param name="Subject">Which thing of that kind — what the mail's subject and body name.</param>
/// <param name="AuditSubject">
/// What the journal records for a transition: equal to <paramref name="Subject"/> for every kind
/// except a drifted code-integrity report, which also carries its version, count and capped paths.
/// </param>
/// <param name="Transition">What the observation changed, <see cref="AlertTransition.None"/> included.</param>
/// <param name="Detail">The figure or the service manager's words that go in the mail's body.</param>
public sealed record PendingAlert(
    AlertKind Kind,
    string Subject,
    string AuditSubject,
    AlertTransition Transition,
    string Detail);
