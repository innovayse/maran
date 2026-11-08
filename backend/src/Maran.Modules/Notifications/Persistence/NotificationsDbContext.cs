using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Persistence.Configurations;

namespace Maran.Modules.Notifications.Persistence;

/// <summary>
/// The Notifications module's only database context. Owns the <c>notifications</c> PostgreSQL schema
/// exclusively — no other module reads or writes it, and this module never reads another module's
/// schema (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no tenant query filter here, and there must not be one.</b> The one row this schema
/// holds describes the PANEL — the single mail server every module's mail leaves through, the
/// addresses its operator alerts go to, how those alerts last fared, and the administrators' feed.
/// None of it belongs to a customer or has an <c>AccountId</c> to scope by, its whole HTTP surface is
/// <c>[Authorize(AdminOnly)]</c>, and a filter closing over the account of <c>ICurrentUser.Caller</c> would hide
/// the row from the background sender, which runs off a queue with no request and no principal at
/// all. The visible failure would be that password-reset mail stopped being sent whenever nobody was
/// signed in — which is precisely when it is sent.
/// </para>
/// <para>
/// The context takes <see cref="IEncryptionService"/> for exactly one column — the SMTP submission
/// password — which is encrypted at rest through <c>EncryptedStringConverter</c> (rules/csharp.md
/// "Secret encryption at rest"). It is the only encrypted column in this schema, and the reason the
/// schema is worth isolating: a module that does not hold a provider credential cannot leak one.
/// </para>
/// </remarks>
public sealed class NotificationsDbContext : DbContext
{
    /// <summary>The PostgreSQL schema this module owns.</summary>
    public const string SchemaName = "notifications";

    /// <summary>The cipher the SMTP password column is encrypted with at rest.</summary>
    private readonly IEncryptionService _encryptionService;

    /// <summary>Creates the context with options supplied by the Host's DI container.</summary>
    /// <param name="options">EF Core options, including the Npgsql provider and connection string.</param>
    /// <param name="encryptionService">The panel's shared cipher for secrets at rest.</param>
    public NotificationsDbContext(
        DbContextOptions<NotificationsDbContext> options,
        IEncryptionService encryptionService)
        : base(options)
    {
        _encryptionService = encryptionService;
    }

    /// <summary>The panel's outgoing mail configuration; at most one row.</summary>
    public DbSet<SmtpSettings> SmtpSettings
    {
        get
        {
            return Set<SmtpSettings>();
        }
    }

    /// <summary>The addresses operator alerts are mailed to; at most <c>AlertRecipient.MaxCount</c> rows.</summary>
    public DbSet<AlertRecipient> AlertRecipients
    {
        get
        {
            return Set<AlertRecipient>();
        }
    }

    /// <summary>How the last operator alerts fared on each channel; one row per channel.</summary>
    public DbSet<AlertChannelState> AlertChannelStates
    {
        get
        {
            return Set<AlertChannelState>();
        }
    }

    /// <summary>The administrators' notification feed.</summary>
    public DbSet<PanelNotification> PanelNotifications
    {
        get
        {
            return Set<PanelNotification>();
        }
    }

    /// <summary>Which administrator has dismissed which notification.</summary>
    public DbSet<NotificationDismissal> NotificationDismissals
    {
        get
        {
            return Set<NotificationDismissal>();
        }
    }

    /// <summary>Applies the schema and the entity configurations.</summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new SmtpSettingsConfiguration(_encryptionService));
        modelBuilder.ApplyConfiguration(new AlertRecipientConfiguration());
        modelBuilder.ApplyConfiguration(new AlertChannelStateConfiguration());
        modelBuilder.ApplyConfiguration(new PanelNotificationConfiguration());
        modelBuilder.ApplyConfiguration(new NotificationDismissalConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
