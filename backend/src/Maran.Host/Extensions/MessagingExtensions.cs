using JasperFx.CodeGeneration;
using Wolverine;
using Wolverine.Postgresql;

namespace Maran.Host.Extensions;

/// <summary>
/// Wires in-process messaging. Background work — certificate renewal, backups, multi-step
/// provisioning — runs on durable queues stored in the panel's own PostgreSQL, so a restart never
/// loses queued work and no message broker is installed on the customer's server
/// (rules/architecture.md: minimum daemons).
/// </summary>
public static class MessagingExtensions
{
    /// <summary>PostgreSQL schema the durable message store lives in.</summary>
    private const string MessagingSchema = "wolverine";

    /// <summary>Adds the message bus with durable PostgreSQL persistence when a database is configured.</summary>
    /// <param name="builder">The host builder to configure messaging on.</param>
    /// <param name="connectionString">
    /// The panel database connection string. When empty (a shell run without a database), messaging
    /// still starts, but purely in memory — durability requires a database.
    /// </param>
    /// <returns>The same builder, for chaining.</returns>
    public static IHostBuilder AddPanelMessaging(this IHostBuilder builder, string connectionString) =>
        builder.UseWolverine(options =>
        {
            // No handlers ship yet, so there is nothing to compile at runtime. Static type-load
            // mode keeps the Roslyn runtime-compilation dependency out of the deployed panel.
            options.CodeGeneration.TypeLoadMode = TypeLoadMode.Static;

            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.PersistMessagesWithPostgresql(connectionString, MessagingSchema);

                // The message store is Wolverine's OWN infrastructure — its queue and outbox
                // tables in the `wolverine` schema — not the panel's domain schema, so it may
                // create and update itself. Blocking that would leave a fresh installation with
                // no queue tables and nothing to create them: the installer applies the panel's
                // EF migrations, and it has no business knowing a message library's internals.
                //
                // The panel's OWN tables are a different matter: they change only through EF
                // migrations applied deliberately by the installer and the update command, which
                // take a database dump first — never as a side effect of a process start.
            }
        });
}
