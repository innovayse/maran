using Maran.Agent.Client.Services.TransferService;

namespace Maran.Modules.ServerMigration.Models;

/// <summary>An inventory stream that ended in a failure, or that exceeded what the panel will hold.</summary>
/// <param name="Error">The failure as the agent client or the handler stated it.</param>
/// <param name="Reason">Why the source refused, when the agent stated it; <see cref="SourceFailureReason.Unspecified"/> otherwise.</param>
public sealed record FailedInventory(Error Error, SourceFailureReason Reason) : InventoryOutcome;
