using Maran.Modules.Tasks.Domain.Enums;
using Maran.Modules.Tasks.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Tasks.Services;

/// <summary>Resolves the operator-facing name of a task status in the current request's culture.</summary>
/// <remarks>
/// <b>The name is the backend's to produce, not the SPA's</b> (rules/architecture.md "The backend owns
/// the data, the SPA renders it"). Unlike a task kind, the status set is closed and every member has
/// an entry, so a miss is a defect and not a fallback case: the module's tests walk
/// <c>Enum.GetValues</c> against the real resource files and fail on the first member without a name.
/// </remarks>
public sealed class TaskStatusDisplayNames
{
    /// <summary>The prefix a status's resx key carries, so statuses cannot collide with kinds or other names.</summary>
    private const string KeyPrefix = "TaskStatus";

    /// <summary>Resolves this module's display-name resources for the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the resolver.</summary>
    /// <param name="displayNames">This module's display-name resources.</param>
    public TaskStatusDisplayNames(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Names one status as an operator reads it.</summary>
    /// <param name="status">The status to name.</param>
    /// <returns>The localized name.</returns>
    public string Of(PanelTaskStatus status)
    {
        return _displayNames[KeyPrefix + status].Value;
    }
}
