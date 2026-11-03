using Maran.Modules.Tasks.Common;
using Maran.Modules.Tasks.Domain.Enums;
using Maran.Modules.Tasks.Services;

namespace Maran.Modules.Tasks.Queries.ListTaskStatusChoices;

/// <summary>
/// Handles <see cref="ListTaskStatusChoicesQuery"/> by naming every <see cref="PanelTaskStatus"/>
/// member for the request's culture.
/// </summary>
public sealed class ListTaskStatusChoicesQueryHandler
{
    /// <summary>Names each status as an operator reads it, in the request's culture.</summary>
    private readonly TaskStatusDisplayNames _statusNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="statusNames">Names a task status for the request's culture.</param>
    public ListTaskStatusChoicesQueryHandler(TaskStatusDisplayNames statusNames)
    {
        _statusNames = statusNames;
    }

    /// <summary>Returns every status, named, in declaration order.</summary>
    /// <param name="query">The (parameterless) request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the choices; this operation never fails.</returns>
    public Task<Result<IReadOnlyList<TaskStatusChoiceDto>>> HandleAsync(
        ListTaskStatusChoicesQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TaskStatusChoiceDto> choices = Enum.GetValues<PanelTaskStatus>()
            .Select(status =>
            {
                return new TaskStatusChoiceDto(status, _statusNames.Of(status));
            })
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<TaskStatusChoiceDto>>.Ok(choices));
    }
}
