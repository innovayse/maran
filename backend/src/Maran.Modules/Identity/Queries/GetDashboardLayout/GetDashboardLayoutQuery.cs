namespace Maran.Modules.Identity.Queries.GetDashboardLayout;

/// <summary>Reads the signed-in person's own dashboard layout.</summary>
/// <param name="UserId">
/// Whose layout to read. Taken from the caller's own token by the controller and never from the
/// request, so there is no parameter with which to ask for somebody else's.
/// </param>
/// <param name="ForAdministrator">
/// Whether the caller is an administrator, which decides the defaults the layout starts from.
/// Established by the controller from the caller's own token, never from the request.
/// </param>
public sealed record GetDashboardLayoutQuery(Guid UserId, bool ForAdministrator);
