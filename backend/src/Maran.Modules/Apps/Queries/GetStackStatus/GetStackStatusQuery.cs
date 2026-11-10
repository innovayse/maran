namespace Maran.Modules.Apps.Queries.GetStackStatus;

/// <summary>
/// Reads whether the server is set up to serve sites and with what (<c>GET /api/v1/apps/stack</c>).
/// Carries no parameters; readable by any signed-in role.
/// </summary>
public sealed record GetStackStatusQuery;
