using Maran.Agent.Client.Interfaces;
using Maran.Host.Resilience;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Identity.Common.Interfaces;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Sites.Persistence;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Polly.Registry;

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

    /// <summary>Every module database context resolves.</summary>
    [Theory]
    [InlineData(typeof(AccountsDbContext))]
    [InlineData(typeof(IdentityDbContext))]
    [InlineData(typeof(SitesDbContext))]
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
    [InlineData(typeof(AccountsDbContext))]
    [InlineData(typeof(IdentityDbContext))]
    [InlineData(typeof(SitesDbContext))]
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
    public void Every_agent_client_resolves_wrapped_in_its_resilience_pipeline(Type serviceType, Type expectedType)
    {
        using var scope = _factory.Services.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService(serviceType);

        Assert.IsType(expectedType, client);
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
