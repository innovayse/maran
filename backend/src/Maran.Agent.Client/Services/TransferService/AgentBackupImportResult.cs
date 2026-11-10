namespace Maran.Agent.Client.Services.TransferService;

/// <summary>How an import ended: published, or refused and nothing published.</summary>
/// <remarks>
/// Two cases: <see cref="AgentBackupImported"/> and <see cref="AgentBackupImportRefused"/>. The refusal
/// carries a code AND a reason, because a failed signature, a wrong fingerprint and a taken account name are
/// three different things an operator does next, and they arrive as one agent failure.
/// </remarks>
public abstract record AgentBackupImportResult
{
    /// <summary>Closes the hierarchy to the two cases this client declares.</summary>
    private protected AgentBackupImportResult()
    {
    }
}
