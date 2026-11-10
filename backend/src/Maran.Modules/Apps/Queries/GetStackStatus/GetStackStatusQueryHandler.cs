using Maran.Modules.Apps.Common;
using Maran.SharedKernel.Apps;

namespace Maran.Modules.Apps.Queries.GetStackStatus;

/// <summary>
/// Handles <see cref="GetStackStatusQuery"/> by restating <see cref="IStackStatus"/>, the same answer the
/// Sites module's gate reads, so the screen and the enforcement cannot disagree (issue #517).
/// </summary>
public sealed class GetStackStatusQueryHandler
{
    /// <summary>The one place "is the stack ready" is decided.</summary>
    private readonly IStackStatus _stack;

    /// <summary>Creates the handler.</summary>
    /// <param name="stack">The one place "is the stack ready" is decided.</param>
    public GetStackStatusQueryHandler(IStackStatus stack)
    {
        _stack = stack;
    }

    /// <summary>Returns the stack status.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result; this operation never fails.</returns>
    public Task<Result<StackStatusDto>> HandleAsync(GetStackStatusQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var webServer = _stack.WebServer switch
        {
            StackWebServer.Nginx => "nginx",
            StackWebServer.Apache => "apache",
            _ => null,
        };

        return Task.FromResult(Result<StackStatusDto>.Ok(new StackStatusDto(
            _stack.IsReady,
            webServer,
            _stack.PhpVersions,
            _stack.NodeMajors)));
    }
}
