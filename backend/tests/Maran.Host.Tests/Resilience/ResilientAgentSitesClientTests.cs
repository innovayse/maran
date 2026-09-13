using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SitesService;
using Maran.Host.Resilience;
using Polly.Timeout;

namespace Maran.Host.Tests.Resilience;

/// <summary>
/// What the sites decorator DOES, not merely that it is registered. Resolving the right type proves
/// the class is wired; it does not prove that a given method goes through the pipeline, and a method
/// that returns <c>_inner.Call(...)</c> directly — no timeout on a site deletion — is invisible to a
/// container assertion.
/// </summary>
public sealed class ResilientAgentSitesClientTests
{
    /// <summary>Deadline for any test that waits on the pipeline, so a stall fails instead of hanging.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A site descriptor with every field set, to check it arrives unchanged.</summary>
    private static readonly SiteDescriptor Descriptor =
        new(["www.example.com"], SiteBackendKind.Php, "8.3", "127.0.0.1:3000", true);

    /// <summary>Create retries a transport failure through the pipeline.</summary>
    [Fact]
    public async Task Create_retries_a_transport_failure_through_the_pipeline()
    {
        var inner = new RecordingAgentSitesClient { FailuresBeforeSuccess = 1 };

        var result = await Invoke(inner, client =>
        {
            return client.CreateAsync("acc1", "example.com", [], SiteBackendKind.Static, "", "", 0, [], default);
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>Change php version retries a transport failure through the pipeline.</summary>
    [Fact]
    public async Task Change_php_version_retries_a_transport_failure_through_the_pipeline()
    {
        var inner = new RecordingAgentSitesClient { FailuresBeforeSuccess = 1 };

        var result = await Invoke(inner, client =>
        {
            return client.ChangePhpVersionAsync("acc1", "example.com", "8.4", Descriptor, 12, [], false, default);
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>Enable retries a transport failure through the pipeline.</summary>
    [Fact]
    public async Task Enable_retries_a_transport_failure_through_the_pipeline()
    {
        var inner = new RecordingAgentSitesClient { FailuresBeforeSuccess = 1 };

        var result = await Invoke(inner, client =>
        {
            return client.EnableAsync("acc1", "example.com", Descriptor, default);
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>Disable retries a transport failure through the pipeline.</summary>
    [Fact]
    public async Task Disable_retries_a_transport_failure_through_the_pipeline()
    {
        var inner = new RecordingAgentSitesClient { FailuresBeforeSuccess = 1 };

        var result = await Invoke(inner, client =>
        {
            return client.DisableAsync("acc1", "example.com", Descriptor, default);
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>Delete retries a transport failure through the pipeline.</summary>
    [Fact]
    public async Task Delete_retries_a_transport_failure_through_the_pipeline()
    {
        var inner = new RecordingAgentSitesClient { FailuresBeforeSuccess = 1 };

        var result = await Invoke(inner, client =>
        {
            return client.DeleteAsync("acc1", "example.com", "", default);
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>Delete is abandoned when the agent stalls past the operation timeout.</summary>
    /// <remarks>
    /// The defect this repository already found once was a client with no timeout at all: a stuck
    /// unix socket hung the HTTP request that made the call. A deletion is the operation where that
    /// matters most, so it is asserted directly rather than inferred from the registration.
    /// </remarks>
    [Fact]
    public async Task Delete_is_abandoned_when_the_agent_stalls_past_the_operation_timeout()
    {
        var inner = new RecordingAgentSitesClient { Delay = TimeSpan.FromSeconds(10) };
        var client = new ResilientAgentSitesClient(
            inner,
            OperationPipelineRegistry.WithOperationTimeout(1));

        var thrown = Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
        {
            await client.DeleteAsync("acc1", "example.com", "", default);
        });

        await thrown.WaitAsync(TestTimeout);
    }

    /// <summary>Every argument reaches the inner client unchanged.</summary>
    [Fact]
    public async Task Every_argument_reaches_the_inner_client_unchanged()
    {
        var inner = new RecordingAgentSitesClient();
        var client = NewClient(inner);

        await client.CreateAsync(
            "acc1",
            "example.com",
            ["www.example.com"],
            SiteBackendKind.ReverseProxy,
            "8.3",
            "127.0.0.1:3000",
            9,
            [],
            default);

        Assert.Equal("acc1", inner.LastAccountUsername);
        Assert.Equal("example.com", inner.LastDomain);
        Assert.Equal(["www.example.com"], inner.LastAliases);
        Assert.Equal(SiteBackendKind.ReverseProxy, inner.LastKind);
        Assert.Equal("8.3", inner.LastPhpVersion);
        Assert.Equal("127.0.0.1:3000", inner.LastProxyUpstream);

        // The plan's worker budget travels with the creation, because the agent writes the
        // site's php-fpm pool as part of creating it. A resilience wrapper that dropped it would
        // hand every new pool a pm.max_children of zero, which php-fpm refuses outright.
        Assert.Equal(9u, inner.LastMaxChildren);
    }

    /// <summary>The version change forwards the site facts the plan values and the overrides.</summary>
    [Fact]
    public async Task The_version_change_forwards_the_site_facts_the_plan_values_and_the_overrides()
    {
        var inner = new RecordingAgentSitesClient();
        var overrides = new[] { new PhpSettingDto("memory_limit", "256M") };

        await NewClient(inner).ChangePhpVersionAsync(
            "acc1",
            "example.com",
            "8.4",
            Descriptor,
            12,
            overrides,
            true,
            default);

        Assert.Equal("acc1", inner.LastAccountUsername);
        Assert.Equal("example.com", inner.LastDomain);
        Assert.Equal("8.4", inner.LastPhpVersion);
        Assert.Same(Descriptor, inner.LastSite);
        Assert.Equal(12u, inner.LastMaxChildren);
        Assert.Same(overrides, inner.LastSettingOverrides);

        // A `bool` in a positional argument list that decides whether a php-fpm pool is DESTROYED:
        // a wrapper that dropped it would start removing pools an account's other sites still need.
        Assert.True(inner.LastRemovePreviousPool);
    }

    /// <summary>Enable and disable each forward the account the domain and the site.</summary>
    [Fact]
    public async Task Enable_and_disable_each_forward_the_account_the_domain_and_the_site()
    {
        var enabled = new RecordingAgentSitesClient();
        var disabled = new RecordingAgentSitesClient();

        await NewClient(enabled).EnableAsync("acc1", "example.com", Descriptor, default);
        await NewClient(disabled).DisableAsync("acc2", "other.com", Descriptor, default);

        Assert.Equal("acc1", enabled.LastAccountUsername);
        Assert.Equal("example.com", enabled.LastDomain);
        Assert.Same(Descriptor, enabled.LastSite);
        Assert.Equal("acc2", disabled.LastAccountUsername);
        Assert.Equal("other.com", disabled.LastDomain);
        Assert.Same(Descriptor, disabled.LastSite);
    }

    /// <summary>Delete forwards the account and the domain.</summary>
    [Fact]
    public async Task Delete_forwards_the_account_and_the_domain()
    {
        var inner = new RecordingAgentSitesClient();

        await NewClient(inner).DeleteAsync("acc1", "example.com", "", default);

        Assert.Equal("acc1", inner.LastAccountUsername);
        Assert.Equal("example.com", inner.LastDomain);
    }

    /// <summary>The tail forwards its arguments and is not put through the operation timeout.</summary>
    /// <remarks>
    /// Deliberate: a tail exists to stay open while nothing happens, so the operation timeout would
    /// cut off the watch and the retry would replay lines the operator has already read.
    /// </remarks>
    [Fact]
    public async Task The_tail_forwards_its_arguments_and_is_not_put_through_the_operation_timeout()
    {
        var inner = new RecordingAgentSitesClient { Delay = TimeSpan.FromSeconds(2) };
        var client = new ResilientAgentSitesClient(
            inner,
            OperationPipelineRegistry.WithOperationTimeout(1));

        async Task DrainAsync()
        {
            await foreach (var unused in client.TailLogAsync("acc1", "example.com", SiteLogSource.Error, 42, default))
            {
                // The events themselves are the client's contract, not the decorator's.
            }
        }

        await DrainAsync().WaitAsync(TestTimeout);

        Assert.Equal("acc1", inner.LastAccountUsername);
        Assert.Equal("example.com", inner.LastDomain);
        Assert.Equal(SiteLogSource.Error, inner.LastLogSource);
        Assert.Equal(42u, inner.LastHistoryLines);
    }

    /// <summary>Builds the decorator over a pipeline whose timeout is long enough not to interfere.</summary>
    /// <param name="inner">The recording client to wrap.</param>
    /// <returns>The decorator under test.</returns>
    private static ResilientAgentSitesClient NewClient(RecordingAgentSitesClient inner)
    {
        return new ResilientAgentSitesClient(
            inner,
            OperationPipelineRegistry.WithOperationTimeout(30));
    }

    /// <summary>Runs one decorated call under the test deadline.</summary>
    /// <typeparam name="T">The value the call produces.</typeparam>
    /// <param name="inner">The recording client to wrap.</param>
    /// <param name="call">The decorated method to invoke.</param>
    /// <returns>What the call returned.</returns>
    private static async Task<SharedKernel.Results.Result<T>> Invoke<T>(
        RecordingAgentSitesClient inner,
        Func<ResilientAgentSitesClient, Task<SharedKernel.Results.Result<T>>> call)
    {
        return await call(NewClient(inner)).WaitAsync(TestTimeout);
    }
}
