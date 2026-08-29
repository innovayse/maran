using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;
using JasperFx.RuntimeCompiler;
using Maran.Host.Modules;
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
    public static IHostBuilder AddPanelMessaging(this IHostBuilder builder, string connectionString)
    {
        return builder
            .ConfigureServices(services =>
            {
                // Wolverine executes each message through a handler type it builds for that message.
                // Something has to produce those types, and with no generator registered every
                // module request failed at runtime with "No IAssemblyGenerator is registered".
                services.AddSingleton<IAssemblyGenerator, AssemblyGenerator>();
            })
            .UseWolverine(options =>
        {
            // Auto, not Static: Static expects handler types generated ahead of time, and with none
            // present Wolverine silently falls back to a runtime scan — a fallback that logs a
            // warning on every boot while doing exactly what Auto does openly.
            options.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;

            // Handlers resolve their module DbContext from the container. Wolverine prefers to inline
            // dependencies into generated code and refuses container lookups by default, which made
            // every handler fail to build: EF registers DbContext options through a factory lambda
            // that cannot be inlined. Allowing the lookup costs one scoped resolve per message and
            // keeps each module owning its own persistence registration.
            options.ServiceLocationPolicy = ServiceLocationPolicy.AlwaysAllowed;

            // Handlers live in the module assemblies, not in the host, and Wolverine scans only the
            // entry assembly by default — which left every module message with no handler and every
            // module request failing at runtime. The assemblies come from the explicit module
            // registry, so this stays a list, not a scan (rules/architecture.md).
            foreach (var assembly in ModuleRegistry.All.Select(module =>
            {
                return module.GetType().Assembly;
            }).Distinct())
            {
                options.Discovery.IncludeAssembly(assembly);
            }

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
}
