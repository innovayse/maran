using Maran.Modules.Tasks.Common;
using Maran.Modules.Tasks.Domain.Entities;
using Maran.Modules.Tasks.Mappers;
using Maran.Modules.Tasks.Persistence;
using Maran.Modules.Tasks.Services;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Tasks.Queries.ListTasks;

/// <summary>
/// Handles <see cref="ListTasksQuery"/> by reading one page of <c>tasks.PanelTasks</c> within what the
/// caller may see.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no authorisation check here.</b> The context's query filter decides which rows a
/// caller may see — an administrator every task, a customer the tasks recorded against their own
/// account with an audience that admits them — before the count as well as before the page, so a
/// customer's total never counts a row they could not open. Every clause below only narrows that.
/// </para>
/// <para>
/// <b>The log is the one thing this handler decides.</b> Only an administrator is sent it; a
/// customer's rows travel with an empty log (<see cref="PanelTaskMapper"/>).
/// </para>
/// </remarks>
public sealed class ListTasksQueryHandler
{
    /// <summary>The Tasks module's database context, and this module's read boundary.</summary>
    private readonly TasksDbContext _dbContext;

    /// <summary>The authenticated principal, whose administrator status decides whether logs are sent.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Names each task's kind as an operator reads it, in the request's culture.</summary>
    private readonly TaskKindDisplayNames _kindNames;

    /// <summary>Describes a failed task's code as a sentence, in the request's culture.</summary>
    private readonly TaskFailureMessages _failures;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Tasks module's database context.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="kindNames">Names a task's kind for the request's culture.</param>
    /// <param name="failures">Describes a failed task's code for the request's culture.</param>
    public ListTasksQueryHandler(
        TasksDbContext dbContext,
        ICurrentUser currentUser,
        TaskKindDisplayNames kindNames,
        TaskFailureMessages failures)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _kindNames = kindNames;
        _failures = failures;
    }

    /// <summary>
    /// Filters by account, status, kind and start window, searches the subject, and answers the
    /// requested page with the total that matched.
    /// </summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<PanelTaskDto>>> HandleAsync(
        ListTasksQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<PanelTask> tasks = _dbContext.PanelTasks.AsNoTracking();

        if (query.AccountId is { } accountId)
        {
            tasks = tasks.Where(task => task.AccountId == accountId);
        }

        if (query.Status is { } status)
        {
            tasks = tasks.Where(task => task.Status == status);
        }

        if (!query.Kind.IsNone)
        {
            var kind = query.Kind.Value;
            tasks = tasks.Where(task => task.Kind == kind);
        }

        if (query.From is { } from)
        {
            tasks = tasks.Where(task => task.StartedAt >= from);
        }

        if (query.To is { } to)
        {
            tasks = tasks.Where(task => task.StartedAt <= to);
        }

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the
            // wildcards so the term matches literally.
            var pattern = LikePattern.Contains(query.Search.Value);
            tasks = tasks.Where(task => EF.Functions.ILike(task.Subject, pattern, LikePattern.EscapeCharacter));
        }

        var page = await tasks.ToPagedAsync(query, ListTasksSorts.Map, cancellationToken);
        var includeLog = _currentUser.IsAdmin;

        return Result<PagedResult<PanelTaskDto>>.Ok(page.Map(task =>
        {
            return PanelTaskMapper.From(
                task, _kindNames.Of(task.Kind), _failures.Describe(task.ErrorCode), includeLog);
        }));
    }
}
