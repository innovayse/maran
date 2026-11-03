using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Queries.ListInstallablePhpVersions;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Sites.Tests.Queries.ListInstallablePhpVersions;

/// <summary>Behavioral contract of <see cref="ListInstallablePhpVersionsQueryHandler"/>.</summary>
public sealed class ListInstallablePhpVersionsQueryHandlerTests
{
    /// <summary>Listing reports every installable version, newest first, flagging the installed ones.</summary>
    [Fact]
    public async Task Listing_reports_every_installable_version_newest_first_flagging_the_installed_ones()
    {
        var handler = new ListInstallablePhpVersionsQueryHandler(new RecordingAgentPhpClient("8.3", "7.4"));

        var result = await handler.HandleAsync(new ListInstallablePhpVersionsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["8.4", "8.3", "8.2", "8.1", "8.0", "7.4"], result.Value.Select(version => version.Version));
        Assert.Equal(
            ["8.3", "7.4"],
            result.Value.Where(version => version.Installed).Select(version => version.Version));
    }

    /// <summary>Listing carries nothing but the version and its installed flag.</summary>
    [Fact]
    public async Task Listing_carries_nothing_but_the_version_and_its_installed_flag()
    {
        var handler = new ListInstallablePhpVersionsQueryHandler(new RecordingAgentPhpClient("8.3"));

        var result = await handler.HandleAsync(new ListInstallablePhpVersionsQuery(), CancellationToken.None);

        var properties = result.Value[0].GetType().GetProperties().Select(property => property.Name);
        Assert.Equal([nameof(InstallablePhpVersionDto.Version), nameof(InstallablePhpVersionDto.Installed)], properties);
    }

    /// <summary>An agent that cannot answer is a failure, not an empty list.</summary>
    [Fact]
    public async Task An_agent_that_cannot_answer_is_a_failure_not_an_empty_list()
    {
        var handler = new ListInstallablePhpVersionsQueryHandler(
            new RecordingAgentPhpClient(Error.Of("AgentUnavailable", ErrorType.Unavailable)));

        var result = await handler.HandleAsync(new ListInstallablePhpVersionsQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
    }
}
