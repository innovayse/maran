namespace Maran.Agent.Client.Services.BackupService;

/// <summary>The account's whole home and every database the panel knows it owns.</summary>
/// <remarks>
/// It has no members. It is sent as no scope at all, which is the only meaning a request from before
/// scopes existed can have, so a whole restore is byte-for-byte what it always was on the wire.
/// </remarks>
public sealed record AgentWholeRestoreScope : AgentRestoreScope;
