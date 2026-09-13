using Maran.Modules.Tasks.Common;
using Maran.Modules.Tasks.Queries.GetTask;
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
/// It is attributed <see cref="AuthorizationPolicies.AnyAuthenticated"/> rather than
/// <see cref="AuthorizationPolicies.AdminOnly"/>, and that is the opposite of a widening. Every kind
/// of task in v1 is an administrator's operation, and the policy attribute would answer a customer
/// 403 — which tells them there is an administrator-only tasks feed here and that they were refused
/// it. Instead the surface answers 404, the way one customer's site answers 404 to another: the
/// module's own query filter makes the rows invisible and its handlers answer <c>TaskNotFound</c>,
/// so nothing is confirmed (spec §8, rules/testing.md item 3).
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

    /// <summary>Creates the controller with the caller identity, the message bus and the stream.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus queries are dispatched through.</param>
    /// <param name="stream">Resolves a watch request and produces its frames.</param>
    /// <param name="streamWriter">Writes those frames to the caller as server-sent events.</param>
    public TasksController(
        ICurrentUser currentUser,
        IMessageBus bus,
        TaskStreamService stream,
        TaskStreamWriter streamWriter)
        : base(currentUser)
    {
        _bus = bus;
        _stream = stream;
        _streamWriter = streamWriter;
    }

    /// <summary>Lists the panel's most recent background tasks, newest first.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The tasks, or 404 for a caller this surface does not exist for.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PanelTaskDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAllAsync(CancellationToken cancellationToken)
    {
        var query = new ListTasksQuery();
        return ToActionResult(await _bus.InvokeAsync<Result<IReadOnlyList<PanelTaskDto>>>(query, cancellationToken));
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
    /// nothing on the host, and the surface is administrator-only. It bounds how fast panes are
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
