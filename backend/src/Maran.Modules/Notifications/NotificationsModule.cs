using System.Resources;
using Maran.Modules.Notifications.Interfaces;
using Maran.Modules.Notifications.Persistence;
using Maran.Modules.Notifications.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Notifications;

/// <summary>
/// The Notifications module's entry point (rules/csharp.md "Canonical backend layout"). Registers
/// <see cref="NotificationsDbContext"/> against the <c>notifications</c> PostgreSQL schema and
/// contributes the module's controllers to the Host's routing. Owns the panel's one outgoing-mail
/// configuration and the sending of every message that leaves it (spec §11, R12).
/// </summary>
/// <remarks>
/// <para>
/// <b>It is a module of its own because outgoing mail is not a fact about the machine.</b> The
/// settings, the sender and the handler for <see cref="SendMailRequested"/> lived in Monitoring
/// while this was the only module that needed to send. The consequence was that Identity's password
/// reset — a security feature — silently depended on Monitoring being loaded: remove that module and
/// the reset event reached a queue with no handler, and nothing told the operator. Mail is a panel
/// facility with several consumers, so it is a module with several consumers.
/// </para>
/// <para>
/// <b>Consumers reach it through the Sdk and never through this assembly.</b>
/// <see cref="SendMailRequested"/> for sending, <see cref="IAlertRecipientDirectory"/> for the one
/// stored address a background alert needs. <c>IMailer</c> stays module-internal: only this module
/// sends, and a cross-module mail interface would put the credential-holding seam in everybody's
/// reach.
/// </para>
/// </remarks>
public sealed class NotificationsModule : IPanelModule
{
    /// <summary>Configuration key under which the panel's connection string lives.</summary>
    private const string ConnectionStringName = "Panel";

    /// <summary>The embedded resource base name of <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Modules.Notifications.Resources.ErrorMessages";

    /// <summary>The embedded resource base name of <c>Resources/DisplayNames*.resx</c>.</summary>
    private const string DisplayNamesResourceBaseName = "Maran.Modules.Notifications.Resources.DisplayNames";

    /// <inheritdoc />
    public string Name
    {
        get
        {
            return Manifest.Id;
        }
    }

    /// <inheritdoc />
    public Manifest Manifest
    {
        get
        {
            return NotificationsManifest.Instance;
        }
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? string.Empty;

        // Scoped, like every module context. A DbContext is not thread-safe and a singleton one
        // would be shared by every concurrent request and by the background sender besides.
        services.AddDbContext<NotificationsDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // A SINGLETON, unlike everything else here, and deliberately: it is a cache, and a scoped one
        // would be a fresh empty cache per request, which is not a cache. It resolves the scoped
        // context through IServiceScopeFactory rather than capturing one (R12).
        services.AddSingleton<SmtpSettingsCache>();

        // Scoped: the mailer reads the singleton cache but is resolved alongside the handlers that
        // use it, including the background handler's own per-message scope.
        services.AddScoped<IMailer, SmtpMailer>();

        // The one field of the mail settings another module may read. Scoped like the handlers that
        // resolve it, and registered here rather than in the Sdk because a contract's implementation
        // belongs to the module that owns the data.
        services.AddScoped<IAlertRecipientDirectory, AlertRecipientDirectory>();

        // Scoped, because it reads the request's own ICurrentUser for the journal's actor — and
        // records the panel itself as the actor when there is no request, which is most of the time
        // in this module.
        services.AddScoped<NotificationsAuditJournal>();

        // Registered rather than left for the message bus to construct, so the background sender is
        // resolvable — and therefore drivable by a test — rather than exercisable only through a
        // booted bus.
        services.AddScoped<IntegrationEvents.Handlers.SendMailRequestedHandler>();

        // The shared resource pool the panel-wide ResxErrorTextProvider resolves error codes and
        // Manifest.DisplayNameKey against. Module-internal lookups inject IStringLocalizer<T> instead.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(NotificationsModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(NotificationsModule).Assembly));
    }
}
