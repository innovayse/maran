using Maran.Host.Modules;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Databases.Persistence;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Sftp.Persistence;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Ssl.Persistence;
using Maran.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Creates the composed panel's module schemas the way the installer does before first boot, from
/// the same source the code under test reads — <see cref="ModuleRegistry"/> — rather than from a
/// list a fixture keeps by hand.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it is derived.</b> The deletion cascade and the residue audit are both properties of the
/// COMPOSED panel: <c>ModuleAccountResidueAuditor</c> walks every context declared by the assemblies
/// behind <see cref="ModuleRegistry.All"/> and counts what each one still holds. Two fixtures used
/// to migrate a hand-written list of contexts instead, so the subject of the test outgrew its own
/// setup every time a module was composed: when Backups joined, and again when Ftp joined, both
/// fixtures reported the new module as UNCHECKED and had to be repaired by adding one line. Deriving
/// from the registry makes the next module join the fixture by construction, which is the only
/// arrangement that does not depend on somebody remembering.
/// </para>
/// <para>
/// <b>What deriving LOSES, stated rather than papered over.</b> A hand-written list is explicit: a
/// reader saw exactly which schemas existed when a test ran. This does not show that, and it can
/// change what a test does without the test changing — a module composed tomorrow gets its tables
/// created here, and if that module's schema is broken these two suites are where it surfaces. That
/// is the trade this takes deliberately: a fixture that silently migrates one schema too many fails
/// loudly at the migration, while a fixture that silently migrates one too few passes green and
/// certifies a module nobody audited. Only one of those two failure modes lies.
/// </para>
/// <para>
/// <b>What it cannot see.</b> It re-derives the registry query rather than calling the auditor's own
/// private <c>ContextTypes</c>, so it follows the registry but NOT a change to the auditor's rule for
/// reading it — if the auditor one day filtered contexts differently, this would keep migrating the
/// registry's full set and nothing here would notice. It is blind to schemas no module declares (the
/// agent's files on the host, Wolverine's own tables) and to whether a migrated schema is CORRECT;
/// it only asserts the tables were created.
/// </para>
/// </remarks>
internal static class ModuleSchemas
{
    /// <summary>
    /// The contexts these fixtures write rows into or assert against, which a run must therefore
    /// have MIGRATED.
    /// </summary>
    /// <remarks>
    /// A FLOOR, not the enumeration — the derived set is expected to be larger and grows on its own.
    /// It exists because the way derivation goes blind is by quietly returning less: a registry read
    /// through the wrong assembly, a type filter that stops matching, an empty list that migrates
    /// nothing and leaves every assertion counting rows in tables that do not exist. A floor on the
    /// axis that can go quiet turns that into a named failure instead of a green run.
    /// </remarks>
    private static readonly Type[] Written =
    [
        typeof(IdentityDbContext),
        typeof(AccountsDbContext),
        typeof(SitesDbContext),
        typeof(SslDbContext),
        typeof(SftpDbContext),
        typeof(TasksDbContext),
        typeof(BackupsDbContext),
        typeof(DatabasesDbContext),
    ];

    /// <summary>Every module <c>DbContext</c> the composed panel declares.</summary>
    /// <returns>The context types, in the registry's own stable order.</returns>
    /// <remarks>
    /// The same walk <c>ModuleAccountResidueAuditor.ContextTypes</c> performs: the distinct
    /// assemblies behind <see cref="ModuleRegistry.All"/>, every concrete <see cref="DbContext"/> in
    /// them, ordered by full name so a failure names the same context on every run.
    /// </remarks>
    public static Type[] Derive()
    {
        var contexts = ModuleRegistry.All
            .Select(module => { return module.GetType().Assembly; })
            .Distinct()
            .SelectMany(assembly => { return assembly.GetTypes(); })
            .Where(type => { return type.IsSubclassOf(typeof(DbContext)) && !type.IsAbstract; })
            .OrderBy(type => { return type.FullName; }, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            contexts.Length > 0,
            "ModuleSchemas.Derive found NO DbContext in the assemblies behind ModuleRegistry.All. "
                + "The fixture would then migrate nothing and every suite using it would count rows "
                + "in tables that do not exist.");

        return contexts;
    }

    /// <summary>Applies every derived module's migrations, minus the named exclusions.</summary>
    /// <param name="services">The request scope the contexts are resolved from.</param>
    /// <param name="excluded">
    /// Contexts deliberately left unmigrated, each with its reason at the call site. Leaving a
    /// schema out is how these suites manufacture a module that really fails against real
    /// PostgreSQL.
    /// </param>
    /// <returns>A task that completes when the schemas exist.</returns>
    /// <remarks>
    /// <para>
    /// A derived context the composed host cannot RESOLVE is skipped rather than failed, which
    /// mirrors the auditor exactly: <c>ModuleAccountResidueAuditor</c> treats a context its scope
    /// does not build as a module holding nothing, not as a module it failed to check. A fixture
    /// that threw there would refuse to run against a panel the auditor is perfectly happy to audit.
    /// </para>
    /// <para>
    /// That skip is the one way this could go quiet, so the floor is asserted against what was
    /// actually MIGRATED and not merely against what was derived — a context that vanished from the
    /// registry and one that is declared but never registered fail the same way, by name.
    /// </para>
    /// <para>
    /// An exclusion naming a context the registry no longer declares fails by name too: a stale
    /// exemption is a hole the next module falls into, and one that no longer matches anything would
    /// leave the suite believing it had manufactured a failure it had not.
    /// </para>
    /// </remarks>
    public static async Task MigrateAsync(IServiceProvider services, params Type[] excluded)
    {
        var contexts = Derive();

        var stale = excluded
            .Where(exclusion => { return !contexts.Contains(exclusion); })
            .Select(exclusion => { return exclusion.Name; })
            .ToArray();

        Assert.True(
            stale.Length == 0,
            "ModuleSchemas.MigrateAsync was asked to exclude contexts the module registry no longer "
                + "declares: " + string.Join(", ", stale));

        var migrated = new List<Type>();

        foreach (var contextType in contexts)
        {
            if (excluded.Contains(contextType))
            {
                continue;
            }

            if (services.GetService(contextType) is not DbContext context)
            {
                continue;
            }

            await context.Database.MigrateAsync();
            migrated.Add(contextType);
        }

        var unmigrated = Written
            .Where(required => { return !excluded.Contains(required) && !migrated.Contains(required); })
            .Select(required => { return required.Name; })
            .ToArray();

        Assert.True(
            unmigrated.Length == 0,
            "ModuleSchemas.MigrateAsync created no schema for contexts these fixtures write rows "
                + "into: " + string.Join(", ", unmigrated));
    }
}
