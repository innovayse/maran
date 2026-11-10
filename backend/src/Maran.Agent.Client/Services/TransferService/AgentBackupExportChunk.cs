namespace Maran.Agent.Client.Services.TransferService;

/// <summary>A slice of the exported artifact, in order and never empty.</summary>
/// <param name="Data">The bytes, at most 1 MiB per event.</param>
public sealed record AgentBackupExportChunk(ReadOnlyMemory<byte> Data) : AgentBackupExportEvent;
