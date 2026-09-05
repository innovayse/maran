using Maran.Modules.Tasks.Domain.Enums;

namespace Maran.Modules.Tasks.Common;

/// <summary>The payload of the one <c>end</c> event that closes a task stream.</summary>
/// <remarks>
/// It carries the task's final STATUS rather than a reason of its own. A second vocabulary for
/// "how did this end" would be a second definition of the same fact, and the two would drift the
/// first time a status was added — so the stream says exactly what the row says.
/// </remarks>
/// <param name="Status">The final status the task reached.</param>
public sealed record TaskStreamEndDto(PanelTaskStatus Status);
