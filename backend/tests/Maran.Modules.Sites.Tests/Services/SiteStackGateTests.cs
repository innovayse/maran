using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.SharedKernel.Apps;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Sites.Tests.Services;

/// <summary>Behavioral contract of <see cref="SiteStackGate"/> and the <see cref="SiteHostApps"/> it reads.</summary>
public sealed class SiteStackGateTests
{
    /// <summary>The installed web server is translated to the one the agent client speaks.</summary>
    /// <param name="installed">The Apps module's web server.</param>
    /// <param name="expected">The agent client's web server, or null for none.</param>
    [Theory]
    [InlineData(StackWebServer.Nginx, SiteWebServer.Nginx)]
    [InlineData(StackWebServer.Apache, SiteWebServer.Apache)]
    [InlineData(StackWebServer.None, null)]
    public async Task The_installed_web_server_is_translated(StackWebServer installed, SiteWebServer? expected)
    {
        var stack = await new SiteStackGate(new FakeStackStatus(installed)).ReadAsync(CancellationToken.None);

        Assert.Equal(expected, stack.WebServer);
    }

    /// <summary>A stack with a web server is ready and carries no refusal; one without is refused as a conflict.</summary>
    [Fact]
    public async Task A_stack_without_a_web_server_is_refused_as_a_conflict_and_one_with_is_not()
    {
        var ready = await SiteStacks.Ready().ReadAsync(CancellationToken.None);
        var empty = await SiteStacks.NotReady().ReadAsync(CancellationToken.None);

        Assert.Null(ready.NotReadyFailure());
        var refusal = empty.NotReadyFailure();
        Assert.Equal("AppsStackNotReady", refusal!.Code);
        Assert.Equal(ErrorType.Conflict, refusal.Type);
    }

    /// <summary>A runtime is installed only when its exact version is, never a prefix of it.</summary>
    [Fact]
    public async Task A_runtime_is_installed_only_when_its_exact_version_is()
    {
        var stack = await SiteStacks.With(StackWebServer.Nginx, ["8.3"], ["22"]).ReadAsync(CancellationToken.None);

        Assert.True(stack.HasPhp("8.3"));
        Assert.False(stack.HasPhp("8.30"));
        Assert.False(stack.HasPhp("8"));
        Assert.True(stack.HasNode("22"));
        Assert.False(stack.HasNode("2"));
        Assert.False(stack.HasNode("220"));
    }

    /// <summary>A background caller gets the installed web server, and a missing one is a defect that throws.</summary>
    [Fact]
    public async Task A_background_caller_gets_the_installed_web_server_and_a_missing_one_throws()
    {
        Assert.Equal(
            SiteWebServer.Apache,
            await new SiteStackGate(new FakeStackStatus(StackWebServer.Apache)).RequireWebServerAsync(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            return SiteStacks.NotReady().RequireWebServerAsync(CancellationToken.None);
        });
    }

    /// <summary>A cancelled read does not answer.</summary>
    [Fact]
    public async Task A_cancelled_read_does_not_answer()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
        {
            return SiteStacks.Ready().ReadAsync(source.Token);
        });
    }
}
