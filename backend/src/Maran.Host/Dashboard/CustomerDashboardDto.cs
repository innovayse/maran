namespace Maran.Host.Dashboard;

/// <summary>
/// What a customer's landing request is answered with: nothing of the server's state. The shape is
/// deliberately empty. A customer's own landing screen is the client zone (issue #49's subject), and
/// a second one invented here is a thing that issue would then have to undo; the health verdict the
/// screen also carries comes from a route of its own.
/// </summary>
public sealed record CustomerDashboardDto : DashboardDto;
