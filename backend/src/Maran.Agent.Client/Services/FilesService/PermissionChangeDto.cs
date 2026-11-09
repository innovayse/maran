namespace Maran.Agent.Client.Services.FilesService;

/// <summary>What a permission change reached.</summary>
/// <param name="Changed">Entries whose mode was set, or already was.</param>
/// <param name="Skipped">
/// Entries left alone: a link inside a recursive walk, or an entry the account does not own. Non-zero
/// means the change did not reach everything it asked for.
/// </param>
public sealed record PermissionChangeDto(ulong Changed, ulong Skipped);
