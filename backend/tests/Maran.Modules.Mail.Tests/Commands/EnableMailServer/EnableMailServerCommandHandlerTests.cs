using Maran.Modules.Mail.Commands.EnableMailServer;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Mail.Tests.Commands.EnableMailServer;

/// <summary>Behaviour of the handler that turns the mail server on.</summary>
public sealed class EnableMailServerCommandHandlerTests
{
    /// <summary>Enabling a server that was never configured stores the settings as an enabled row.</summary>
    [Fact]
    public async Task Enabling_a_never_configured_server_stores_the_settings_as_an_enabled_row()
    {
        using var context = new MailTestContext();

        var result = await Handler(context).HandleAsync(
            MailServerCommands.Enable(pop3: true, antivirus: true, external: true, sizeMib: 80),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = await context.Database.MailServerSettings.SingleAsync(CancellationToken.None);
        Assert.True(row.Enabled);
        Assert.Equal("mail.example.com", row.Hostname);
        Assert.True(row.Pop3Enabled);
        Assert.True(row.AntivirusEnabled);
        Assert.True(row.AllowExternalForwarding);
        Assert.Equal(80, row.MaxMessageMib);
        Assert.Equal(4, row.SpamGreylistScore);
        Assert.Equal(6, row.SpamAddHeaderScore);
        Assert.Equal(15, row.SpamRejectScore);
    }

    /// <summary>Enabling sends the settings to the agent in one call, with no webmail master hash.</summary>
    [Fact]
    public async Task Enabling_sends_the_settings_to_the_agent_in_one_call()
    {
        using var context = new MailTestContext();

        await Handler(context).HandleAsync(
            MailServerCommands.Enable(pop3: true, sizeMib: 80),
            CancellationToken.None);

        Assert.Equal(["Enable(mail.example.com,pop3=True,av=False,size=80)"], context.Agent.Calls);
    }

    /// <summary>The host name is stored and sent in lower case.</summary>
    [Fact]
    public async Task The_host_name_is_stored_and_sent_in_lower_case()
    {
        using var context = new MailTestContext();

        await Handler(context).HandleAsync(
            MailServerCommands.Enable(hostname: "Mail.Example.COM"),
            CancellationToken.None);

        var row = await context.Database.MailServerSettings.SingleAsync(CancellationToken.None);
        Assert.Equal("mail.example.com", row.Hostname);
        Assert.StartsWith("Enable(mail.example.com,", context.Agent.Calls[0], StringComparison.Ordinal);
    }

    /// <summary>The answer carries the settings next to what the agent observed, and the ports to open.</summary>
    [Fact]
    public async Task The_answer_carries_the_settings_beside_the_observation()
    {
        using var context = new MailTestContext();

        var result = await Handler(context).HandleAsync(MailServerCommands.Enable(), CancellationToken.None);

        Assert.NotNull(result.Value.Settings);
        Assert.True(result.Value.Settings!.Enabled);
        Assert.Equal("mail.example.com", result.Value.EffectiveHostname);
        Assert.Contains(587, result.Value.Ports);
    }

    /// <summary>Enabling a server that was turned off replaces the settings on the one row.</summary>
    [Fact]
    public async Task Enabling_a_server_that_was_turned_off_replaces_the_settings_on_the_one_row()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();
        var row = await context.Database.MailServerSettings.SingleAsync(CancellationToken.None);
        row.Disable(context.Clock.UtcNow);
        await context.Database.SaveChangesAsync(CancellationToken.None);

        await Handler(context).HandleAsync(
            MailServerCommands.Enable(hostname: "mx.example.org", sizeMib: 10),
            CancellationToken.None);

        var rows = await context.Database.MailServerSettings.ToListAsync(CancellationToken.None);
        var single = Assert.Single(rows);
        Assert.True(single.Enabled);
        Assert.Equal("mx.example.org", single.Hostname);
        Assert.Equal(10, single.MaxMessageMib);
    }

    /// <summary>Enabling records an audit entry naming the host name.</summary>
    [Fact]
    public async Task Enabling_records_an_audit_entry_naming_the_host_name()
    {
        using var context = new MailTestContext();

        await Handler(context).HandleAsync(MailServerCommands.Enable(), CancellationToken.None);

        var entry = Assert.Single(context.Entries);
        Assert.Equal("MailServerEnabled", entry.Action);
        Assert.Equal("mail.example.com", entry.Subject);
        Assert.Equal(MailServerCommands.Address, entry.IpAddress);
        Assert.Equal(MailServerCommands.Client, entry.UserAgent);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A refusal by the agent is passed through, writes no row and is journaled as a failure.</summary>
    [Fact]
    public async Task A_refusal_by_the_agent_is_passed_through_writes_no_row_and_is_journaled_as_a_failure()
    {
        using var context = new MailTestContext();
        context.Agent.FailNext("Enable", Error.Of("AgentValidationFailed", ErrorType.Validation));

        var result = await Handler(context).HandleAsync(MailServerCommands.Enable(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentValidationFailed", result.Error!.Code);
        Assert.Empty(await context.Database.MailServerSettings.ToListAsync(CancellationToken.None));
        var entry = Assert.Single(context.Entries);
        Assert.Equal("MailServerEnabled", entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>Builds the handler from the context's parts.</summary>
    /// <param name="context">The test context.</param>
    /// <returns>The handler.</returns>
    private static EnableMailServerCommandHandler Handler(MailTestContext context)
    {
        return new EnableMailServerCommandHandler(context.Database, context.Agent, context.Journal, context.Clock);
    }
}
