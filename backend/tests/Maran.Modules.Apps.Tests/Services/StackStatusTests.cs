using Maran.Modules.Apps.Domain.Entities;
using Maran.Modules.Apps.Domain.ValueObjects;
using Maran.Modules.Apps.Services;
using Maran.Modules.Apps.Tests.TestSupport;
using Maran.SharedKernel.Apps;

namespace Maran.Modules.Apps.Tests.Services;

/// <summary>The first-run gate's one source of truth: only installed apps count, and a web server makes the stack ready.</summary>
public sealed class StackStatusTests
{
    /// <summary>The instant every row is created at.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Adds a row for <paramref name="text"/>, installed or still installing.</summary>
    /// <param name="context">The ledger.</param>
    /// <param name="text">The catalogue id.</param>
    /// <param name="installed">Whether the install finished.</param>
    private static void Add(Maran.Modules.Apps.Persistence.AppsDbContext context, string text, bool installed)
    {
        Assert.True(AppId.TryParse(text, out var appId));
        var row = InstalledApp.BeginInstall(Guid.NewGuid(), appId, Guid.NewGuid(), Now);
        if (installed)
        {
            row.MarkInstalled("1", Now);
        }

        context.InstalledApps.Add(row);
    }

    /// <summary>A fresh server has no web server, so the stack is not ready.</summary>
    [Fact]
    public void An_empty_ledger_is_not_ready()
    {
        using var context = AppsTestContext.Create();

        var stack = new StackStatus(context);

        Assert.False(stack.IsReady);
        Assert.Equal(StackWebServer.None, stack.WebServer);
        Assert.Empty(stack.PhpVersions);
        Assert.Empty(stack.NodeMajors);
    }

    /// <summary>A runtime alone does not make the stack ready: sites need a web server.</summary>
    [Fact]
    public void Runtimes_without_a_web_server_are_not_ready()
    {
        using var context = AppsTestContext.Create();
        Add(context, "php-8.3", installed: true);
        context.SaveChanges();

        Assert.False(new StackStatus(context).IsReady);
    }

    /// <summary>A web server that is still installing is not ready: its daemon is not there yet.</summary>
    [Fact]
    public void A_web_server_that_is_still_installing_is_not_ready()
    {
        using var context = AppsTestContext.Create();
        Add(context, "nginx", installed: false);
        context.SaveChanges();

        Assert.False(new StackStatus(context).IsReady);
    }

    /// <summary>An installed web server makes the stack ready, and runtimes are listed newest first.</summary>
    [Fact]
    public void An_installed_web_server_makes_the_stack_ready_and_runtimes_are_newest_first()
    {
        using var context = AppsTestContext.Create();
        Add(context, "apache", installed: true);
        Add(context, "php-8.1", installed: true);
        Add(context, "php-8.4", installed: true);
        Add(context, "php-7.4", installed: false);
        Add(context, "nodejs-20", installed: true);
        Add(context, "nodejs-22", installed: true);
        context.SaveChanges();

        var stack = new StackStatus(context);

        Assert.True(stack.IsReady);
        Assert.Equal(StackWebServer.Apache, stack.WebServer);
        Assert.Equal(["8.4", "8.1"], stack.PhpVersions);
        Assert.Equal(["22", "20"], stack.NodeMajors);
    }
}
