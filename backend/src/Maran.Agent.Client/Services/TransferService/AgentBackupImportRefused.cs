using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Services.TransferService;

/// <summary>An import that did not publish: the typed failure and why the package was refused.</summary>
/// <param name="Error">The typed failure, a code and never the agent's own sentence.</param>
/// <param name="Reason">Why the package was refused, or <see cref="BackupImportRefusal.Unspecified"/> for a failure of the host.</param>
public sealed record AgentBackupImportRefused(Error Error, BackupImportRefusal Reason) : AgentBackupImportResult;
