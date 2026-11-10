namespace Maran.Modules.Mail.Queries.GetMailQueueCount;

/// <summary>
/// Asks how many messages the host's mail queue holds. The figure behind the navigation badge
/// (issue #320).
/// </summary>
/// <remarks>
/// Takes no parameters, and so has no validator: there is one mail stack on the host and the call behind
/// it accepts nothing from a caller, so there is no member whose bounds could need holding.
/// </remarks>
public sealed record GetMailQueueCountQuery;
