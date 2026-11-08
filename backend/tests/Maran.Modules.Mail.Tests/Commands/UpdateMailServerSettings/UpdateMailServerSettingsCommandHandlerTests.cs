using Maran.Modules.Mail.Commands.UpdateMailServerSettings;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Mail.Tests.Commands.UpdateMailServerSettings;

/// <summary>Behaviour of the handler that re-states the settings of a server that is on.</summary>
public sealed class UpdateMailServerSettingsCommandHandlerTests
{
    /// <summary>Updating a server that is on re-states the settings on the row and through the agent.</summary>
    [Fact]
    public async Task Updating_a_server_that_is_on_restates_the_settings_on_the_row_and_through_the_agent()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();

        var result = await Handler(context).HandleAsync(
            MailServerCommands.Update(pop3: true, antivirus: true, external: true, sizeMib: 90, greylist: 3, header: 8, reject: 40),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = await context.Database.MailServerSettings.SingleAsync(CancellationToken.None);
        Assert.True(row.Enabled);
        Assert.True(row.Pop3Enabled);
        Assert.True(row.AntivirusEnabled);
        Assert.True(row.AllowExternalForwarding);
        Assert.Equal(90, row.MaxMessageMib);
        Assert.Equal(3, row.SpamGreylistScore);
        Assert.Equal(8, row.SpamAddHeaderScore);
        Assert.Equal(40, row.SpamRejectScore);
        Assert.Equal(["Enable(mail.example.com,pop3=True,av=True,size=90)"], context.Agent.Calls);
    }

    /// <summary>Updating records a settings-updated audit entry.</summary>
    [Fact]
    public async Task Updating_records_a_settings_updated_audit_entry()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();

        await Handler(context).HandleAsync(MailServerCommands.Update(), CancellationToken.None);

        var entry = Assert.Single(context.Entries);
        Assert.Equal("MailServerSettingsUpdated", entry.Action);
        Assert.Equal("mail.example.com", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>Updating a server that was never configured is refused as not enabled, before the agent is asked.</summary>
    [Fact]
    public async Task Updating_a_server_that_was_never_configured_is_refused_before_the_agent_is_asked()
    {
        using var context = new MailTestContext();

        var result = await Handler(context).HandleAsync(MailServerCommands.Update(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailServerErrorCodes.MailNotEnabled, result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(context.Agent.Calls);
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>Updating a server that was turned off is refused and does not switch it back on.</summary>
    [Fact]
    public async Task Updating_a_server_that_was_turned_off_is_refused_and_does_not_switch_it_on()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();
        var row = await context.Database.MailServerSettings.SingleAsync(CancellationToken.None);
        row.Disable(context.Clock.UtcNow);
        await context.Database.SaveChangesAsync(CancellationToken.None);

        var result = await Handler(context).HandleAsync(MailServerCommands.Update(sizeMib: 10), CancellationToken.None);

        Assert.Equal(MailServerErrorCodes.MailNotEnabled, result.Error!.Code);
        Assert.Empty(context.Agent.Calls);
        var stored = await context.Database.MailServerSettings.AsNoTracking().SingleAsync(CancellationToken.None);
        Assert.False(stored.Enabled);
        Assert.Equal(50, stored.MaxMessageMib);
    }

    /// <summary>A refusal by the agent leaves the stored settings as they were and is journaled as a failure.</summary>
    [Fact]
    public async Task A_refusal_by_the_agent_leaves_the_stored_settings_untouched()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();
        context.Agent.FailNext("Enable", Error.Of("AgentValidationFailed", ErrorType.Validation));

        var result = await Handler(context).HandleAsync(MailServerCommands.Update(sizeMib: 10), CancellationToken.None);

        Assert.Equal("AgentValidationFailed", result.Error!.Code);
        var stored = await context.Database.MailServerSettings.AsNoTracking().SingleAsync(CancellationToken.None);
        Assert.Equal(50, stored.MaxMessageMib);
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>Builds the handler from the context's parts.</summary>
    /// <param name="context">The test context.</param>
    /// <returns>The handler.</returns>
    private static UpdateMailServerSettingsCommandHandler Handler(MailTestContext context)
    {
        return new UpdateMailServerSettingsCommandHandler(
            context.Database, context.Agent, context.Journal, context.Clock);
    }
}
