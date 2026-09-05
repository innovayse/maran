namespace Maran.Agent.Client.Services.DbService;

/// <summary>One row of the agent's DIAGNOSTIC database listing: what this server holds under a name
/// that decodes to the account.</summary>
/// <param name="DatabaseName">
/// Fully-qualified database name as the server holds it. Always present: it is the one thing the
/// listing actually establishes.
/// </param>
/// <param name="DbUsername">
/// Fully-qualified name of the database's dedicated user, or null when the sender did not establish
/// it. The agent always leaves it unset, because the server records which users are GRANTED on a
/// database rather than which one was "its" user, and the customer names the two halves
/// independently. Null and an empty name are different answers and no caller may conflate them: the
/// true pairing lives in the panel's own rows.
/// </param>
/// <param name="SizeBytes">
/// On-disk size in bytes, or null when the sender did not measure it. The agent always leaves it
/// unset, because a size is one <c>information_schema</c> query per database and a listing that
/// measured every row would scan the whole server's table metadata. Null is not zero: zero would be
/// the claim "this database is empty", which the listing never made. Ask <c>GetSizeAsync</c> for a
/// real figure.
/// </param>
/// <remarks>
/// This is not an authorisation answer. The server has no notion of a tenant, so a name only looks
/// like it belongs to an account because of the prefix this panel put there; what a customer may
/// see, drop or measure is decided by the panel's own rows, never by this listing.
/// </remarks>
public sealed record DatabaseSummaryDto(string DatabaseName, string? DbUsername, ulong? SizeBytes);
