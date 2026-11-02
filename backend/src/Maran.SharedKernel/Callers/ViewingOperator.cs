namespace Maran.SharedKernel.Callers;

/// <summary>
/// The operator actually at the keyboard while they view the panel as another login (issue #317).
/// </summary>
/// <param name="UserId">The operator's own panel user id.</param>
/// <param name="Username">The operator's own login name, as the audit journal records it.</param>
public sealed record ViewingOperator(Guid UserId, string Username);
