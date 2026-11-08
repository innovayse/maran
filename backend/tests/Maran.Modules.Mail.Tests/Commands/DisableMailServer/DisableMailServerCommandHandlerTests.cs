using Maran.Modules.Mail.Commands.DisableMailServer;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Mail.Tests.Commands.DisableMailServer;

/// <summary>Behaviour of the handler that turns the mail server off.</summary>
public sealed class DisableMailServerCommandHandlerTests
{
    /// <summary>Disabling stops the daemons and records that the panel no longer wants them.</summary>
    [Fact]
    public async Task Disabling_stops_the_daemons_and_records_that_the_panel_no_longer_wants_them()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();

        var result = await Handler(context).HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["Disable()"], context.Agent.Calls);
        var row = await context.Database.MailServerSettings.SingleAsync(CancellationToken.None);
        Assert.False(row.Enabled);
        Assert.False(result.Value.Settings!.Enabled);
    }

    /// <summary>Disabling keeps the host name and every setting on the row.</summary>
    [Fact]
    public async Task Disabling_keeps_the_host_name_and_the_settings()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();

        await Handler(context).HandleAsync(Command(), CancellationToken.None);

        var row = await context.Database.MailServerSettings.SingleAsync(CancellationToken.None);
        Assert.Equal("mail.example.com", row.Hostname);
        Assert.Equal(50, row.MaxMessageMib);
    }

    /// <summary>Disabling a server that never enabled mail is not refused and writes no row.</summary>
    [Fact]
    public async Task Disabling_a_never_configured_server_is_not_refused_and_writes_no_row()
    {
        using var context = new MailTestContext();

        var result = await Handler(context).HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Settings);
        Assert.Empty(await context.Database.MailServerSettings.ToListAsync(CancellationToken.None));
        Assert.Equal(["Disable()"], context.Agent.Calls);
    }

    /// <summary>Disabling records an audit entry naming the host name.</summary>
    [Fact]
    public async Task Disabling_records_an_audit_entry_naming_the_host_name()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();

        await Handler(context).HandleAsync(Command(), CancellationToken.None);

        var entry = Assert.Single(context.Entries);
        Assert.Equal("MailServerDisabled", entry.Action);
        Assert.Equal("mail.example.com", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A refusal by the agent keeps the row enabled and is journaled as a failure.</summary>
    [Fact]
    public async Task A_refusal_by_the_agent_keeps_the_row_enabled_and_is_journaled_as_a_failure()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();
        context.Agent.FailNext("Disable", Error.Of("AgentSystemFailure", ErrorType.Failure));

        var result = await Handler(context).HandleAsync(Command(), CancellationToken.None);

        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        var row = await context.Database.MailServerSettings.AsNoTracking().SingleAsync(CancellationToken.None);
        Assert.True(row.Enabled);
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>Builds the command the tests send.</summary>
    /// <returns>The command.</returns>
    private static DisableMailServerCommand Command()
    {
        return new DisableMailServerCommand(MailServerCommands.Address, MailServerCommands.Client);
    }

    /// <summary>Builds the handler from the context's parts.</summary>
    /// <param name="context">The test context.</param>
    /// <returns>The handler.</returns>
    private static DisableMailServerCommandHandler Handler(MailTestContext context)
    {
        return new DisableMailServerCommandHandler(context.Database, context.Agent, context.Journal, context.Clock);
    }
}
