using Maran.Modules.Tasks.Common;
using Maran.Modules.Tasks.Queries.CountFailedTasks;
using Maran.Modules.Tasks.Queries.GetTask;
using Maran.Modules.Tasks.Queries.ListRunningTasks;
using Maran.Modules.Tasks.Queries.ListTaskKindChoices;
using Maran.Modules.Tasks.Queries.ListTasks;
using Maran.Modules.Tasks.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Tasks.Controllers;

/// <summary>
/// HTTP surface for the panel's background tasks. Thin by design (rules/csharp.md "Controller shape
/// is fixed"): binds the request, dispatches through Wolverine, translates the
/// <see cref="Result{T}"/>. No business logic, no data access.
///
/// It is attributed <see cref="AuthorizationPolicies.AnyAuthenticated"/> because both roles use it.
/// An administrator reads every task. A customer reads the tasks recorded against their own account
/// — the backups, restores and certificates they started, or that ran for them — with the log
/// withheld, because the log is operator material. Server-wide tasks and what an administrator does
/// to an account carry no account and stay administrator-only. Another tenant's task answers 404
/// rather than 403: the module's query filter makes the row invisible and its handlers answer
/// <c>TaskNotFound</c>, so nothing is confirmed (spec §8, rules/testing.md item 3).
/// </summary>
[Route("api/v1/tasks")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("Tasks")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class TasksController : BaseApiController
{
    /// <summary>The message bus queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Resolves a watch request and produces its frames.</summary>
    private readonly TaskStreamService _stream;

    /// <summary>Writes those frames to the caller as server-sent events.</summary>
    private readonly TaskStreamWriter _streamWriter;

    /// <summary>Produces the frames of the shell's one running-tasks stream.</summary>
    private readonly RunningTasksStreamService _runningStream;

    /// <summary>Writes those frames to the caller as server-sent events.</summary>
    private readonly RunningTasksStreamWriter _runningStreamWriter;

    /// <summary>Creates the controller with the caller identity, the message bus and the stream.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus queries are dispatched through.</param>
    /// <param name="stream">Resolves a watch request and produces its frames.</param>
    /// <param name="streamWriter">Writes those frames to the caller as server-sent events.</param>
    /// <param name="runningStream">Produces the frames of the shell's one running-tasks stream.</param>
    /// <param name="runningStreamWriter">Writes the running-tasks frames as server-sent events.</param>
    public TasksController(
        ICurrentUser currentUser,
        IMessageBus bus,
        TaskStreamService stream,
        TaskStreamWriter streamWriter,
        RunningTasksStreamService runningStream,
        RunningTasksStreamWriter runningStreamWriter)
        : base(currentUser)
    {
        _bus = bus;
        _stream = stream;
        _streamWriter = streamWriter;
        _runningStream = runningStream;
        _runningStreamWriter = runningStreamWriter;
    }

    /// <summary>
    /// Reads one page of the background tasks the caller may see, newest first unless a sort is named,
    /// optionally searched and narrowed by account, status, kind and a window of start times.
    /// </summary>
    /// <param name="query">
    /// Page, page size, search, sort and the <c>accountId</c>, <c>status</c>, <c>kind</c>, <c>from</c>
    /// and <c>to</c> filters, bound from the query string; <c>ListTasksQueryValidator</c> holds each to
    /// its bounds.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The page with the total that matched — every task for an administrator, their own for a
    /// customer — or a 400 naming the bound that was broken.
    /// </returns>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<PanelTaskDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAllAsync([FromQuery] ListTasksQuery query, CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<PagedResult<PanelTaskDto>>>(query, cancellationToken));
    }

    /// <summary>
    /// Lists every running task the caller may see, newest first, in one read. The shell's badge
    /// watches <c>running/stream</c> instead.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The running tasks.</returns>
    [HttpGet("running")]
    [ProducesResponseType(typeof(IReadOnlyList<PanelTaskDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetRunningAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<IReadOnlyList<PanelTaskDto>>>(
            new ListRunningTasksQuery(), cancellationToken));
    }

    /// <summary>
    /// Watches every running task the caller may see on ONE server-sent event stream: a <c>task</c>
    /// event for each of them as it stands and each time one changes (the change that finishes it
    /// included), and a <c>running</c> event naming the running ids whenever that set changes. This is
    /// what the shell's badge and tray open, once per signed-in shell, instead of a stream per task.
    /// </summary>
    /// <remarks>
    /// Visibility is the module's query filter, as on every other read here, so a customer is streamed
    /// their own account's tasks and nothing else. The stream never ends on its own; the caller's
    /// disconnect ends it. Rate-limited like the per-task stream, for the reason given there.
    /// </remarks>
    /// <param name="cancellationToken">Cancelled when the subscriber disconnects.</param>
    /// <returns>The event stream.</returns>
    [HttpGet("running/stream")]
    [Produces(TaskStreamWriter.EventStreamContentType)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetRunningStreamAsync(CancellationToken cancellationToken)
    {
        await _runningStreamWriter.WriteAsync(Response, _runningStream.ReadAsync(cancellationToken), cancellationToken);
        return new EmptyResult();
    }

    /// <summary>Counts the failed tasks the caller may see, for the list's attention notice.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The count, as a bare number.</returns>
    [HttpGet("failed-count")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetFailedCountAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<int>>(new CountFailedTasksQuery(), cancellationToken));
    }

    /// <summary>Lists the kinds among the tasks the caller may see, named for their language, for the kind filter.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The choices, ordered by name.</returns>
    [HttpGet("kinds")]
    [ProducesResponseType(typeof(IReadOnlyList<TaskKindChoiceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetKindsAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<IReadOnlyList<TaskKindChoiceDto>>>(
            new ListTaskKindChoicesQuery(), cancellationToken));
    }

    /// <summary>Reads one task. A task the caller may not see answers 404, not 403.</summary>
    /// <param name="id">The task to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The task, or 404.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PanelTaskDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<PanelTaskDto>>(new GetTaskQuery(id), cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Watches one task as server-sent events: a <c>task</c> event each time it changes, then
    /// exactly one <c>end</c> event naming the status it reached. A task the caller may not see
    /// answers 404, not 403.
    /// </summary>
    /// <remarks>
    /// The stream is bound to this request. When the watcher disconnects, the request's cancellation
    /// token stops the reader with it — and stops nothing else: the OPERATION being watched carries
    /// on, which is the entire reason it was recorded as a task rather than awaited on a request.
    ///
    /// It runs under the ordinary <c>api</c> rate-limit policy rather than a concurrency limiter of
    /// its own. That is a smaller risk than it is for a site-log tail, which pins a blocking reader
    /// in the root daemon per open stream: this one holds a database poll every half second and
    /// nothing on the host, and a customer can only open it on their own tasks. It bounds how fast panes are
    /// opened rather than how many are held open, which is stated here so the next person to add a
    /// stream does not read the omission as a decision that concurrency does not matter.
    /// </remarks>
    /// <param name="id">The task to watch.</param>
    /// <param name="cancellationToken">Cancelled when the watcher disconnects.</param>
    /// <returns>The event stream, or a problem response when the task is not there.</returns>
    [HttpGet("{id:guid}/stream")]
    [Produces(TaskStreamWriter.EventStreamContentType)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetStreamAsync(Guid id, CancellationToken cancellationToken)
    {
        var target = await _stream.ResolveAsync(id, cancellationToken);
        if (!target.IsSuccess)
        {
            return ToActionResult(target);
        }

        // Written directly to the response rather than returned as a value, because the body is
        // produced over time: an IActionResult carrying a materialized value would have to wait for
        // a stream that only ends when the task does.
        await _streamWriter.WriteAsync(Response, _stream.ReadAsync(target.Value, cancellationToken), cancellationToken);
        return new EmptyResult();
    }
}
