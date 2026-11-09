using System.Resources;
using Maran.Modules.Mail.Interfaces;
using Maran.Modules.Mail.Persistence;
using Maran.Modules.Mail.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Mail;

/// <summary>
/// The Mail module's entry point (rules/csharp.md "Canonical backend layout"). Registers
/// <see cref="MailDbContext"/> against the <c>mail</c> PostgreSQL schema and contributes the module's
/// controllers to the Host's routing. Owns the mail the panel hosts for its customers — the server switch,
/// the domains, mailboxes and aliases, and the DNS records they need. It does NOT own the panel's own
/// outgoing mail (SMTP settings, alerts, password-reset mails), which is the Notifications module's: the two
/// share a word and nothing else.
/// </summary>
public sealed class MailModule : IPanelModule
{
    /// <summary>Configuration key under which the panel's connection string lives.</summary>
    private const string ConnectionStringName = "Panel";

    /// <summary>The embedded resource base name of <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Modules.Mail.Resources.ErrorMessages";

    /// <summary>The embedded resource base name of <c>Resources/DisplayNames*.resx</c>.</summary>
    private const string DisplayNamesResourceBaseName = "Maran.Modules.Mail.Resources.DisplayNames";

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
            return MailManifest.Instance;
        }
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? string.Empty;

        // Scoped by default: the context carries the request's tenant scope, and a singleton DbContext is
        // shared mutable state across concurrent requests whatever it holds.
        services.AddDbContext<MailDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // A singleton, because it holds no state: it opens its own scope per call, so the Host can run the
        // module's migrations through the root provider (rules/architecture.md "The database evolves the same way").
        services.AddSingleton<IModuleMigrations, MailModuleMigrations>();

        // Scoped, because it reads the request's own tenant-scoped context: this module's answer to the
        // personal-data export, an allow-list of fields and never a secret.
        services.AddScoped<IPersonalDataProvider, MailPersonalDataProvider>();

        // Scoped, because it reads the request's own ICurrentUser for the journal's actor.
        services.AddScoped<MailAuditJournal>();

        // Scoped, because it takes a transaction on the request's own DbContext connection: the advisory lock
        // it holds is scoped to that transaction and released by its commit.
        services.AddScoped<IMailboxSlotGate, MailboxSlotGate>();

        // This module's answer to how many mailboxes an account owns, for the Accounts module's resource summary.
        services.AddScoped<IAccountResourceCounter, MailAccountResourceCounter>();

        // A singleton: generating a key holds no state. It is a seam so tests need not make a real RSA key.
        services.AddSingleton<IDkimKeyGenerator, DkimKeyGenerator>();

        // Scoped, because each reads the request's context or the tenant-scoped account window.
        services.AddScoped<MailServerReader>();
        services.AddScoped<MailPasswordHashing>();
        services.AddScoped<MailAccountStates>();

        // Scoped, because it reads and publishes through the request's own context and the agent: the one path
        // an alias change takes to the host.
        services.AddScoped<MailAliasSetPublisher>();

        // The shared resource pool the panel-wide ResxErrorTextProvider resolves error codes and
        // Manifest.DisplayNameKey against. Module-internal lookups inject IStringLocalizer<T> directly instead.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(MailModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(MailModule).Assembly));
    }
}
