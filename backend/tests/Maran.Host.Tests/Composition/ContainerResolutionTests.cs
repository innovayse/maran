using Maran.Agent.Client.Interfaces;
using Maran.Host.Modules;
using Maran.Host.Resilience;
using Maran.Host.Tests.Resilience;
using Maran.Modules.Identity.Common.Interfaces;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Polly.Registry;
using Polly.Timeout;

namespace Maran.Host.Tests.Composition;

/// <summary>
/// Every service the composed host must be able to hand out. A missing registration is invisible
/// to the HTTP tests: a type nothing has requested yet fails only when something finally requests
/// it, which in practice is at boot on a customer's server. IEncryptionService had been
/// unregistered since the host was written, and stayed harmless until the first encrypted column
/// appeared.
/// </summary>
public sealed class ContainerResolutionTests : IClassFixture<PanelTestFactory>
{
    /// <summary>
    /// Outer guard for a call the pipeline is expected to abandon. Far longer than the composed
    /// host's one-second operation timeout and its two retries, so a failure here means the call was
    /// never abandoned at all rather than that the deadline was tight.
    /// </summary>
    private static readonly TimeSpan ComposedTimeoutDeadline = TimeSpan.FromSeconds(30);

    private readonly PanelTestFactory _factory;
    /// <summary>Creates the fixture.</summary>

    public ContainerResolutionTests(PanelTestFactory factory)
    {
        _factory = factory;
    }

    /// <summary>The encryption service resolves.</summary>
    [Fact]
    public void The_encryption_service_resolves()
    {
        using var scope = _factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IEncryptionService>());
    }

    /// <summary>The password hasher resolves.</summary>
    [Fact]
    public void The_password_hasher_resolves()
    {
        using var scope = _factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPasswordHasher>());
    }

    /// <summary>Every identity service resolves.</summary>
    [Theory]
    [InlineData(typeof(IAccessTokenIssuer))]
    [InlineData(typeof(ISessionService))]
    [InlineData(typeof(IAuditWriter))]
    [InlineData(typeof(ITotpService))]
    [InlineData(typeof(IRecoveryCodeService))]
    [InlineData(typeof(IAgentAccountsClient))]
    [InlineData(typeof(IAccountDirectory))]
    public void Every_identity_service_resolves(Type serviceType)
    {
        using var scope = _factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService(serviceType));
    }

    /// <summary>Every database context every compiled-in module contributes.</summary>
    /// <remarks>
    /// Read off the module registry rather than written out, for the reason the IDOR fixtures assert
    /// their own completeness: a hand-written list goes stale the first time a module is added, and it
    /// goes stale SILENTLY — the theory keeps passing on the rows it still has. The list this replaced
    /// named three contexts while the registry contributed six, so <c>SslDbContext</c>,
    /// <c>DatabasesDbContext</c> and <c>SftpDbContext</c> had nothing asserting either that they
    /// resolve or that they are scoped to one request.
    /// </remarks>
    /// <returns>The context types, one theory row each.</returns>
    public static TheoryData<Type> ModuleDatabaseContexts()
    {
        var contexts = ModuleRegistry.All
            .Select(module =>
            {
                return module.GetType().Assembly;
            })
            .Distinct()
            .SelectMany(assembly =>
            {
                return assembly.GetTypes();
            })
            .Where(type =>
            {
                return typeof(DbContext).IsAssignableFrom(type) && !type.IsAbstract;
            })
            .OrderBy(type =>
            {
                return type.FullName;
            }, StringComparer.Ordinal);

        var rows = new TheoryData<Type>();
        foreach (var context in contexts)
        {
            rows.Add(context);
        }

        return rows;
    }

    /// <summary>The registry contributes a database context for every compiled in module.</summary>
    [Fact]
    public void The_registry_contributes_a_database_context_for_every_compiled_in_module()
    {
        // A reflection-driven theory that finds nothing passes silently, which is the "no tests found
        // is a failure" rule applied one level down (rules/testing.md). One context per module is the
        // rule this product is built on — each module owns exactly one schema and one context
        // (rules/architecture.md) — so the count is the assertion, not merely non-emptiness.
        Assert.Equal(ModuleRegistry.All.Count, ModuleDatabaseContexts().Count);
    }

    /// <summary>Every module database context resolves.</summary>
    /// <param name="contextType">The module database context resolved from the container.</param>
    [Theory]
    [MemberData(nameof(ModuleDatabaseContexts))]
    public void Every_module_database_context_resolves(Type contextType)
    {
        using var scope = _factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService(contextType));
    }

    /// <summary>Every module database context is scoped to one request.</summary>
    /// <remarks>
    /// The lifetime is the mechanism, not a detail. SitesDbContext closes its tenant query filter
    /// over the request's ICurrentUser, so a singleton would capture whichever caller happened to
    /// build it first and then serve that person's tenant scope to everybody — every other
    /// customer's sites included. Registering it Singleton passed all 452 tests before this test
    /// existed, including the whole HTTP IDOR fixture, because every one of those tests builds its
    /// own scope and never asks whether a second scope gets a second instance.
    ///
    /// Asserted behaviourally rather than by reading the ServiceDescriptor: what matters is that
    /// two requests do not share one context, which is what these two assertions say directly.
    /// </remarks>
    /// <param name="contextType">The module database context resolved from the container.</param>
    [Theory]
    [MemberData(nameof(ModuleDatabaseContexts))]
    public void Every_module_database_context_is_scoped_to_one_request(Type contextType)
    {
        using var first = _factory.Services.CreateScope();
        using var second = _factory.Services.CreateScope();

        var withinOneScope = first.ServiceProvider.GetRequiredService(contextType);
        var againWithinTheSameScope = first.ServiceProvider.GetRequiredService(contextType);
        var inAnotherScope = second.ServiceProvider.GetRequiredService(contextType);

        Assert.Same(withinOneScope, againWithinTheSameScope);
        Assert.NotSame(withinOneScope, inAnotherScope);
    }

    /// <summary>Every agent client resolves wrapped in its resilience pipeline.</summary>
    /// <remarks>
    /// The pipeline was registered and used by nobody: account creation, suspension and deletion
    /// ran with no timeout, so a stuck unix socket hung the HTTP request that made the call. The
    /// decoration is invisible from every call site by design, which is exactly why it needs a
    /// test that looks at the container — nothing else would notice if a client were registered
    /// undecorated again. Each client is listed here so one forgotten decorator fails on its own
    /// row rather than hiding behind another that is still wired.
    /// </remarks>
    /// <param name="serviceType">The client contract resolved from the container.</param>
    /// <param name="expectedType">The decorator the container must hand back.</param>
    [Theory]
    [InlineData(typeof(IAgentAccountsClient), typeof(ResilientAgentAccountsClient))]
    [InlineData(typeof(IAgentSitesClient), typeof(ResilientAgentSitesClient))]
    [InlineData(typeof(IAgentSslClient), typeof(ResilientAgentSslClient))]
    [InlineData(typeof(IAgentPhpClient), typeof(ResilientAgentPhpClient))]
    [InlineData(typeof(IAgentFilesClient), typeof(ResilientAgentFilesClient))]
    [InlineData(typeof(IAgentDbClient), typeof(ResilientAgentDbClient))]
    [InlineData(typeof(IAgentSftpClient), typeof(ResilientAgentSftpClient))]
    public void Every_agent_client_resolves_wrapped_in_its_resilience_pipeline(Type serviceType, Type expectedType)
    {
        using var scope = _factory.Services.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService(serviceType);

        Assert.IsType(expectedType, client);
    }

    /// <summary>The database and sftp clients the container hands out apply the pipelines timeout.</summary>
    /// <remarks>
    /// The type check above says the decorator is in place; this says the decorator DOES something,
    /// which is a different question and the one this repository got wrong before. The composed
    /// decorator is re-created here around an inner client that never returns, using the container's
    /// own <see cref="ResiliencePipelineProvider{TKey}"/> — the registry the host built, with the
    /// host's configured operation timeout — so a pipeline registered without a timeout strategy, or
    /// a decorator that forwards without executing through it, fails here rather than in production
    /// as a request that never ends.
    ///
    /// The inner client cannot be substituted inside the container itself: the decoration happens
    /// while <c>Program</c> registers services, and a test replacing the registration afterwards
    /// would replace the decorator too, leaving nothing of the composition to test.
    /// </remarks>
    /// <returns>A task that completes when both calls have been abandoned.</returns>
    [Fact]
    public async Task The_database_and_sftp_clients_the_container_hands_out_apply_the_pipelines_timeout()
    {
        using var scope = _factory.Services.CreateScope();
        var pipelines = scope.ServiceProvider.GetRequiredService<ResiliencePipelineProvider<string>>();

        var database = new ResilientAgentDbClient(new RecordingAgentDbClient { Hangs = true }, pipelines);
        var sftp = new ResilientAgentSftpClient(new RecordingAgentSftpClient { Hangs = true }, pipelines);

        await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
        {
            await database.ListAsync("alice", default).WaitAsync(ComposedTimeoutDeadline);
        });
        await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
        {
            await sftp.DeleteAsync("alice", "web", default).WaitAsync(ComposedTimeoutDeadline);
        });
    }

    /// <summary>Both named agent pipelines are registered.</summary>
    [Fact]
    public void Both_named_agent_pipelines_are_registered()
    {
        using var scope = _factory.Services.CreateScope();
        var pipelines = scope.ServiceProvider.GetRequiredService<ResiliencePipelineProvider<string>>();

        Assert.NotNull(pipelines.GetPipeline(AgentCallPipeline.Name));
        Assert.NotNull(pipelines.GetPipeline(AgentOperationPipeline.Name));
    }
}
