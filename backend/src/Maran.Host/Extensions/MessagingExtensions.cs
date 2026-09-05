using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;
using JasperFx.RuntimeCompiler;
using Maran.Host.Modules;
using Wolverine;
using Wolverine.FluentValidation;
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

            // Runs each command's validator before its handler. A validator that nothing invokes is
            // worse than no validator: it is tested, it passes, and it gives everyone reading the
            // module the impression the input is checked. This line is what makes
            // CompleteSetupCommandValidator's password policy real.
            options.UseFluentValidation();

            // Handlers live in the module assemblies, not in the host, and Wolverine scans only the
            // entry assembly by default — which left every module message with no handler and every
            // module request failing at runtime. The assemblies come from the explicit module
            // registry, so this stays a list, not a scan (rules/architecture.md).
            //
            // `Discovery.DisableConventionalDiscovery()` looks like the way to make that list the
            // ONLY source, and it is not: in Wolverine 6 it disables type scanning outright,
            // including the assemblies named below, so every module message loses its handler.
            // Measured, not assumed — with it in place, 201 of 233 integration tests failed and
            // `CertificateRenewalSchedulingTests` named the cause. What remains of the default is a
            // scan of the ENTRY assembly, Maran.Host, and that is closed by
            // `HandlerLocationTests` instead: the Host declares no handler, so the default scan has
            // nothing to find, and a handler written there fails CI rather than silently becoming a
            // route nobody registered.
            //
            // When the Licensing module lands, this list becomes the LICENSED modules rather than
            // the composed ones: a module whose licence lapses must lose its live handlers, not
            // merely its menu entry. That is a change to one expression here, which is the point of
            // having a single source.
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

                // Local queues stay IN-MEMORY, and that is a security decision rather than a
                // default nobody revisited. `UseDurableLocalQueues()` would make every in-process
                // publish survive a restart, which is worth having for a lost ban or a lost alert —
                // and it persists the ENVELOPE BODY, which for `PasswordResetRequested` is a
                // password-reset token. Measured, not assumed: with it on,
                // `PasswordResetEndpointTests.The_token_bearing_envelope_is_never_written_to_the_message_store`
                // found the token sitting in a `wolverine` table that outlives the request, which is
                // exactly what rules/security.md item 8 forbids for anything that ACTS as a secret.
                //
                // So durability is a per-message decision, never a blanket policy: a message that
                // must not be lost declares its own durable local queue, and a message that carries
                // a secret must not be one of them (rules/csharp.md).
            }
        });
    }
}
