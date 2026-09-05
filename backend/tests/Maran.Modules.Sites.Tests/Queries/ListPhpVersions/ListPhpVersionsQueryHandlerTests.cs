using Maran.Modules.Sites.Queries.ListPhpVersions;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Sites.Tests.Queries.ListPhpVersions;

/// <summary>Behavioral contract of <see cref="ListPhpVersionsQueryHandler"/>.</summary>
public sealed class ListPhpVersionsQueryHandlerTests
{
    /// <summary>Listing php versions reports what the host has installed.</summary>
    [Fact]
    public async Task Listing_php_versions_reports_what_the_host_has_installed()
    {
        var handler = new ListPhpVersionsQueryHandler(new RecordingAgentPhpClient("8.3", "8.4"));

        var result = await handler.HandleAsync(new ListPhpVersionsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var versions = result.Value.Select(version =>
        {
            return version.Version;
        });
        Assert.Equal(["8.3", "8.4"], versions);
    }

    /// <summary>Listing php versions never carries the fpm socket path outward.</summary>
    [Fact]
    public async Task Listing_php_versions_never_carries_the_fpm_socket_path_outward()
    {
        // The agent's own DTO has one; the module's does not, deliberately. A filesystem path is
        // operator-facing detail and must not reach a customer's screen (rules/security.md).
        var handler = new ListPhpVersionsQueryHandler(new RecordingAgentPhpClient("8.3"));

        var result = await handler.HandleAsync(new ListPhpVersionsQuery(), CancellationToken.None);

        var properties = result.Value[0].GetType().GetProperties().Select(property =>
        {
            return property.Name;
        });
        Assert.Equal(["Version", "IsDefault"], properties);
    }

    /// <summary>An unknown default stays unknown rather than becoming false.</summary>
    [Fact]
    public async Task An_unknown_default_stays_unknown_rather_than_becoming_false()
    {
        // Null and false are different answers: the agent does not currently establish the host's
        // default CLI PHP, and "not known" must not render as "not the default".
        var handler = new ListPhpVersionsQueryHandler(new RecordingAgentPhpClient("8.3"));

        var result = await handler.HandleAsync(new ListPhpVersionsQuery(), CancellationToken.None);

        Assert.Null(result.Value[0].IsDefault);
    }

    /// <summary>An agent that cannot answer is a failure not an empty list.</summary>
    [Fact]
    public async Task An_agent_that_cannot_answer_is_a_failure_not_an_empty_list()
    {
        // An empty list would tell the SPA "this server has no PHP", which is a different and
        // wrong thing to show a customer whose agent is merely unreachable.
        var handler = new ListPhpVersionsQueryHandler(new RecordingAgentPhpClient(Error.Of("AgentUnavailable", ErrorType.Unavailable)));

        var result = await handler.HandleAsync(new ListPhpVersionsQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
    }
}
