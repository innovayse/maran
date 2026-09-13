using Maran.Modules.Firewall.Options;
using Maran.Modules.Firewall.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Firewall.Tests.TestSupport;

/// <summary>
/// Builds isolated <see cref="FirewallDbContext"/> instances and the options every mutation carries.
/// </summary>
/// <remarks>
/// Each context gets its own uniquely-named in-memory database unless a caller passes a shared name,
/// which is what a test spanning two contexts needs — a handler writing through one and an assertion
/// reading through another, the way a request and the screen after it do.
/// </remarks>
public static class FirewallTestContext
{
    /// <summary>Creates a context over a fresh database, or over the named one.</summary>
    /// <param name="databaseName">The in-memory database to open; a fresh one when omitted.</param>
    /// <param name="interceptor">An interceptor to watch the context's reads with, when a test does.</param>
    /// <returns>The context.</returns>
    public static FirewallDbContext Create(string? databaseName = null, IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<FirewallDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString());

        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new FirewallDbContext(builder.Options);
    }

    /// <summary>The host facts a well-configured panel was given.</summary>
    /// <param name="sshPorts">The raw value of <c>Firewall__SshPorts</c>.</param>
    /// <param name="panelPort">The panel's public port.</param>
    /// <returns>The options, wrapped as the handlers receive them.</returns>
    public static IOptions<FirewallOptions> Options(string sshPorts = "22,2222", int panelPort = 8443)
    {
        return Microsoft.Extensions.Options.Options.Create(
            new FirewallOptions { SshPorts = sshPorts, PanelPort = panelPort });
    }
}
