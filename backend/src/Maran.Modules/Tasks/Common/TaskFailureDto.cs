namespace Maran.Modules.Tasks.Common;

/// <summary>Why a task failed: the sentence a person reads, and the code beside it.</summary>
/// <remarks>
/// One type for the two because they are one fact seen from two sides, and two nullable fields on
/// <see cref="PanelTaskDto"/> could disagree about whether the task failed at all. The sentence is
/// resolved when the row is READ, for the reader's culture, from the code the row stores — never
/// stored itself — so a task that failed under one language reads in whatever language the person
/// looking at it has chosen (rules/csharp.md "The backend owns all user-facing message text").
/// </remarks>
/// <param name="Code">
/// The machine-stable code the operation failed with. For behaviour and for quoting in a support
/// request; never a key a client looks text up by.
/// </param>
/// <param name="Message">
/// The failure as a sentence in the request's culture: the code's own error message, or the Tasks
/// module's generic sentence when no loaded module describes the code.
/// </param>
public sealed record TaskFailureDto(string Code, string Message);
