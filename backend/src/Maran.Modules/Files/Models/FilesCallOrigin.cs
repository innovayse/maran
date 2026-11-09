namespace Maran.Modules.Files.Models;

/// <summary>Where a request came from, as the audit journal records it.</summary>
/// <remarks>
/// A carrier between the module's own layers: the caller's address and user agent are established by the
/// server from the connection, never taken from a request body, and every operation passes them on so
/// the journal entry names the same origin whether the operation succeeded or was refused.
/// </remarks>
/// <param name="IpAddress">The caller's address.</param>
/// <param name="UserAgent">The caller's user agent.</param>
public sealed record FilesCallOrigin(string IpAddress, string UserAgent);
