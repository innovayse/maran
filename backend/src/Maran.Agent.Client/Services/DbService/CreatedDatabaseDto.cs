namespace Maran.Agent.Client.Services.DbService;

/// <summary>What creating a database produced on the server.</summary>
/// <param name="DatabaseName">
/// Fully-qualified database name as created, e.g. <c>acc12345_shop</c>. The caller sent only the
/// suffix — the agent applies the account prefix itself — so this is the panel's first sight of the
/// real name and the name its own row must store.
/// </param>
/// <param name="DbUsername">
/// Fully-qualified name of the dedicated user, namespaced the same way. Recorded by the panel
/// because the agent's listing deliberately cannot establish which user belongs to which database
/// (see <see cref="DatabaseSummaryDto"/>).
/// </param>
/// <remarks>
/// Carries no password, and none may be added: the panel mints one, shows it once and forgets it,
/// and nothing in the agent contract hands one back.
/// </remarks>
public sealed record CreatedDatabaseDto(string DatabaseName, string DbUsername);
