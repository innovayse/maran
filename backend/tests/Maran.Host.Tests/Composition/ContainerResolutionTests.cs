using Maran.Agent.Client.Interfaces;
using Maran.Host.Resilience;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Identity.Common.Interfaces;
using Maran.Modules.Identity.Persistence;
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
    public void Every_identity_service_resolves(Type serviceType)
    {
        using var scope = _factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService(serviceType));
    }

    /// <summary>Every module database context resolves.</summary>
    [Theory]
    [InlineData(typeof(AccountsDbContext))]
    [InlineData(typeof(IdentityDbContext))]
    public void Every_module_database_context_resolves(Type contextType)
    {
        using var scope = _factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService(contextType));
    }

    /// <summary>The agent accounts client resolves wrapped in its resilience pipeline.</summary>
    /// <remarks>
    /// The pipeline was registered and used by nobody: account creation, suspension and deletion
    /// ran with no timeout, so a stuck unix socket hung the HTTP request that made the call. The
    /// decoration is invisible from every call site by design, which is exactly why it needs a
    /// test that looks at the container — nothing else would notice if it were unregistered again.
    /// </remarks>
    [Fact]
    public void The_agent_accounts_client_resolves_wrapped_in_its_resilience_pipeline()
    {
        using var scope = _factory.Services.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<IAgentAccountsClient>();

        Assert.IsType<ResilientAgentAccountsClient>(client);
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
