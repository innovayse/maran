using Maran.Agent.Client.Interfaces;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Sftp.Tests.TestSupport;

/// <summary>
/// An <see cref="IAgentSftpClient"/> double that records every call and answers whatever a test told
/// it to.
/// </summary>
public sealed class RecordingAgentSftpClient : IAgentSftpClient
{
    /// <summary>Every creation this client was asked for, in order.</summary>
    public List<AgentCreateCall> Creates { get; } = [];

    /// <summary>Every delete this client was asked for, in order.</summary>
    public List<AgentDeleteCall> Deletes { get; } = [];

    /// <summary>Every password change this client was asked for, in order.</summary>
    public List<AgentSetPasswordCall> PasswordChanges { get; } = [];

    /// <summary>What <see cref="CreateAsync"/> answers; success with the prefixed login by default.</summary>
    public Result<string>? CreateResult { get; set; }

    /// <summary>What <see cref="DeleteAsync"/> answers; success by default.</summary>
    public Result<bool>? DeleteResult { get; set; }

    /// <summary>What <see cref="SetPasswordAsync"/> answers; success by default.</summary>
    public Result<bool>? SetPasswordResult { get; set; }

    /// <inheritdoc/>
    public Task<Result<string>> CreateAsync(
        string accountUsername,
        string sftpUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        Creates.Add(new AgentCreateCall(accountUsername, sftpUsername, password));

        // The default answer applies the prefix the real agent applies, so a test that never
        // configures a result still exercises the handler's "record what the agent reported" path
        // rather than a name the handler could have rebuilt for itself.
        return Task.FromResult(CreateResult ?? Result<string>.Ok($"{accountUsername}_{sftpUsername}"));
    }

    /// <inheritdoc/>
    public Task<Result<bool>> SetPasswordAsync(
        string accountUsername,
        string sftpUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        PasswordChanges.Add(new AgentSetPasswordCall(accountUsername, sftpUsername, password));

        return Task.FromResult(SetPasswordResult ?? Result<bool>.Ok(true));
    }

    /// <inheritdoc/>
    public Task<Result<bool>> DeleteAsync(
        string accountUsername,
        string sftpUsername,
        CancellationToken cancellationToken)
    {
        Deletes.Add(new AgentDeleteCall(accountUsername, sftpUsername));

        return Task.FromResult(DeleteResult ?? Result<bool>.Ok(true));
    }
}
