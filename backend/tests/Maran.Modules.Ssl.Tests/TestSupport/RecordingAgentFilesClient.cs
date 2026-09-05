using Maran.Agent.Client.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// An <see cref="IAgentFilesClient"/> double recording every file the panel asked the agent to write
/// or delete on a customer's behalf.
/// </summary>
public sealed class RecordingAgentFilesClient : IAgentFilesClient
{
    /// <summary>The refusal to answer writes with, or null to succeed.</summary>
    private readonly Error? _writeFailure;

    /// <summary>Every write, in order: whose home, which path, what content, and with what mode.</summary>
    public List<(string Account, string Path, string Content, uint Mode)> Writes { get; } = [];

    /// <summary>Every delete, in order.</summary>
    public List<(string Account, string Path, bool Recursive)> Deletes { get; } = [];

    /// <summary>Creates a client whose writes succeed, or one whose writes refuse.</summary>
    /// <param name="writeFailure">The refusal to answer writes with, or null to succeed.</param>
    public RecordingAgentFilesClient(Error? writeFailure = null)
    {
        _writeFailure = writeFailure;
    }

    /// <inheritdoc />
    public Task<Result<ulong>> WriteFileAsync(
        string accountUsername,
        string path,
        string content,
        uint mode,
        CancellationToken cancellationToken)
    {
        Writes.Add((accountUsername, path, content, mode));

        return Task.FromResult(_writeFailure is null
            ? Result<ulong>.Ok((ulong)content.Length)
            : Result<ulong>.Fail(_writeFailure));
    }

    /// <inheritdoc />
    public Task<Result<bool>> DeleteEntryAsync(
        string accountUsername,
        string path,
        bool recursive,
        CancellationToken cancellationToken)
    {
        Deletes.Add((accountUsername, path, recursive));
        return Task.FromResult(Result<bool>.Ok(true));
    }
}
