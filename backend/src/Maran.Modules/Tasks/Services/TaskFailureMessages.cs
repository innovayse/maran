using Maran.Modules.Tasks.Common;
using Maran.Modules.Tasks.Resources;

namespace Maran.Modules.Tasks.Services;

/// <summary>
/// Describes a stored task failure for the current request's culture: the sentence a person reads,
/// with the stored code beside it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The same resolution every problem response uses, not a second one.</b> A task fails with an
/// error code some module raised — the Tasks module's own, Ssl's, Backups', the agent client's — and
/// the sentence for that code is the module's own <c>ErrorMessages</c> entry, read through the one
/// <see cref="IErrorTextProvider"/> the panel composes from every module's tables. That is exactly
/// how the code's sentence reaches a caller as an RFC 7807 <c>title</c>, so a failure reads the same
/// on a task as in the refusal that caused it, and no table of task sentences exists to drift from
/// the modules' own (issue #272: the SPA used to keep one, and a code built in another file never
/// reached it).
/// </para>
/// <para>
/// <b>Resolved at read time, never stored.</b> A task outlives the request that started it; storing
/// the sentence would freeze it in that request's language, and a reader who switched language
/// would read the old one. The row keeps the code, and every reader — the list, the running list,
/// the single read and each frame of a subscriber's stream — is answered in its own culture.
/// </para>
/// <para>
/// <b>A code no loaded table describes reads as a generic sentence, not as the code.</b> A row may
/// have been written by a module that has since been removed, or by a build that named a code this
/// one no longer ships. The reader is then told that the operation did not finish and that the code
/// beside it identifies why — <c>TaskFailureUnexplained</c> — rather than being handed a machine
/// constant where a sentence belongs. Every code a module of this build can raise has an entry,
/// which the integration suite's census asserts against the composed panel.
/// </para>
/// </remarks>
public sealed class TaskFailureMessages
{
    /// <summary>The panel's one resolution from an error code to its sentence.</summary>
    private readonly IErrorTextProvider _errorTexts;

    /// <summary>Creates the describer.</summary>
    /// <param name="errorTexts">The panel's error-message resolution, composed from every module's tables.</param>
    public TaskFailureMessages(IErrorTextProvider errorTexts)
    {
        _errorTexts = errorTexts;
    }

    /// <summary>Describes one stored failure, or nothing when the task has not failed.</summary>
    /// <param name="errorCode">The code the row stores, or <c>null</c> while it has not failed.</param>
    /// <returns>The code and its sentence in the request's culture, or <c>null</c>.</returns>
    public TaskFailureDto? Describe(string? errorCode)
    {
        if (errorCode is null)
        {
            return null;
        }

        var message = _errorTexts.TryResolve(errorCode, out var text)
            ? text
            : _errorTexts.Resolve(nameof(ErrorMessages.TaskFailureUnexplained));

        return new TaskFailureDto(errorCode, message);
    }
}
