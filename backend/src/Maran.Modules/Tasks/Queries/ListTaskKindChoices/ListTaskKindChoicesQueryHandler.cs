using Maran.Modules.Tasks.Common;
using Maran.Modules.Tasks.Persistence;
using Maran.Modules.Tasks.Services;

namespace Maran.Modules.Tasks.Queries.ListTaskKindChoices;

/// <summary>
/// Handles <see cref="ListTaskKindChoicesQuery"/> by reading the distinct kinds of the tasks the
/// caller may see and naming each for the request's culture.
/// </summary>
/// <remarks>
/// The answer is as long as the vocabulary of kinds, not as long as the table: the database returns
/// one row per distinct kind. It is ordered by the name the reader reads, which only exists after the
/// resource lookup, so the ordering happens here and not in SQL.
/// </remarks>
public sealed class ListTaskKindChoicesQueryHandler
{
    /// <summary>The Tasks module's database context, and this module's read boundary.</summary>
    private readonly TasksDbContext _dbContext;

    /// <summary>Names each kind as an operator reads it, in the request's culture.</summary>
    private readonly TaskKindDisplayNames _kindNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Tasks module's database context.</param>
    /// <param name="kindNames">Names a task's kind for the request's culture.</param>
    public ListTaskKindChoicesQueryHandler(TasksDbContext dbContext, TaskKindDisplayNames kindNames)
    {
        _dbContext = dbContext;
        _kindNames = kindNames;
    }

    /// <summary>Returns every kind among the tasks the caller may see, named and ordered by name.</summary>
    /// <param name="query">The (parameterless) request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the choices; this operation never fails.</returns>
    public async Task<Result<IReadOnlyList<TaskKindChoiceDto>>> HandleAsync(
        ListTaskKindChoicesQuery query,
        CancellationToken cancellationToken)
    {
        var kinds = await _dbContext.PanelTasks
            .AsNoTracking()
            .Select(task => task.Kind)
            .Distinct()
            .ToListAsync(cancellationToken);

        IReadOnlyList<TaskKindChoiceDto> choices = kinds
            .Select(kind =>
            {
                return new TaskKindChoiceDto(kind, _kindNames.Of(kind));
            })
            .OrderBy(
                choice =>
                {
                    return choice.KindName;
                },
                StringComparer.CurrentCulture)
            .ThenBy(
                choice =>
                {
                    return choice.Kind;
                },
                StringComparer.Ordinal)
            .ToList();

        return Result<IReadOnlyList<TaskKindChoiceDto>>.Ok(choices);
    }
}
