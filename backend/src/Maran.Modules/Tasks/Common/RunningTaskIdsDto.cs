namespace Maran.Modules.Tasks.Common;

/// <summary>The payload of a <c>running</c> event on the shell's running-tasks stream.</summary>
/// <remarks>
/// Ids only, because every task in the set has already been sent whole in its own <c>task</c> event.
/// The set is what lets a subscriber drop a task it holds as running that it may no longer see — a
/// row whose final state it will never be sent — and reconcile what it held after a reconnect.
/// </remarks>
/// <param name="Ids">Every running task the subscriber may see.</param>
public sealed record RunningTaskIdsDto(IReadOnlyList<Guid> Ids);
