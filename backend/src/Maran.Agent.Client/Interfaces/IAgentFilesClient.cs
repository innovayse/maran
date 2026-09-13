using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// The panel's view of the agent's operations on a customer's own files. Deliberately the two the
/// panel needs and no more: the API "spawns nothing at all" and never touches a customer's disk
/// (rules/security.md item 3), so a file inside a document root is written by the agent under that
/// account's uid or it is not written.
/// </summary>
/// <remarks>
/// Introduced by the TLS work, whose ACME HTTP-01 challenge token lives at
/// <c>&lt;document root&gt;/.well-known/acme-challenge/&lt;token&gt;</c> — inside the customer's home,
/// therefore a customer file. The whole FilesService is not modelled here; a module that needs
/// listing, moving or archiving adds the method it needs when it needs it (YAGNI,
/// rules/architecture.md).
/// </remarks>
public interface IAgentFilesClient
{
    /// <summary>Writes one file under an account's home, as that account.</summary>
    /// <param name="accountUsername">System username of the owning account, whose uid the write runs under.</param>
    /// <param name="path">Destination path, relative to the account home; the agent canonicalizes and contains it.</param>
    /// <param name="content">The file's entire content. Small payloads only — this sends a single chunk.</param>
    /// <param name="mode">Permission bits for the created or overwritten file, e.g. <c>0644</c> as octal.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>How many bytes the agent wrote, or a typed failure.</returns>
    Task<Result<ulong>> WriteFileAsync(
        string accountUsername,
        string path,
        string content,
        uint mode,
        CancellationToken cancellationToken);

    /// <summary>Deletes one file or directory under an account's home, as that account.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="path">Path to delete, relative to the account home.</param>
    /// <param name="recursive">Whether a non-empty directory may be removed with its contents.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure — including <c>AgentNotFound</c> for a path already gone.</returns>
    Task<Result<bool>> DeleteEntryAsync(
        string accountUsername,
        string path,
        bool recursive,
        CancellationToken cancellationToken);
}
