namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>One progress line a task was given.</summary>
/// <param name="TaskId">The task.</param>
/// <param name="Percent">The progress.</param>
/// <param name="Line">The line.</param>
public sealed record RecordedTaskLine(Guid TaskId, int Percent, string Line);
