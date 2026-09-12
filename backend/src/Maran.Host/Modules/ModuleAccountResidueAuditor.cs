using System.Linq.Expressions;
using System.Reflection;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Maran.Host.Modules;

/// <summary>
/// The panel's <see cref="IAccountResidueAuditor"/>: asks every composed module's own mapping what
/// it still stores against an account.
/// </summary>
/// <remarks>
/// <para>
/// <b>It lives in the Host because only the Host knows all the modules.</b> A module may not
/// reference another (rules/architecture.md, enforced by <c>ModuleIsolationTests</c>), so no module
/// could ask this question and the module that publishes the cascade least of all. The Host composes
/// them and can.
/// </para>
/// <para>
/// <b>It asks the MODEL, not a list.</b> The census walks each module's <c>DbContext.Model</c> for
/// entities carrying an <c>AccountId</c>, exactly as <c>AccountCascadeTests</c> does, so a module
/// added later — or a marketplace module this assembly was never compiled knowing about — is audited
/// without anybody extending anything. The one thing a maintained list would add is the ability to
/// forget a module, which is the defect this exists for.
/// </para>
/// <para>
/// <b>"What it still stores against an account" means the rows that NAME the account.</b> An entity
/// scoped through a parent instead — <c>SiteHostname</c>, whose owner is read through its site — has
/// no <c>AccountId</c> for this census to match, so it is not counted as residue and its absence is
/// not proof of its absence. Nothing leaks today: such a row is removed by the database's own
/// cascade from the parent this auditor DOES count, so a surviving child implies a surviving parent
/// and the parent is what gets named. The narrower statement is the honest one, and it is written
/// here because <c>TenantScopeTests</c> no longer shares this criterion: its census follows required
/// relationships as well, and a reader who assumed all three call sites still asked the same question
/// would credit this one with reach it does not have.
/// </para>
/// <para>
/// <b>The query filters are bypassed, deliberately.</b> A filter governs what a REQUEST may see, and
/// this is not a request for rows: it is the audit of an account already authorised for deletion. A
/// filtered count would answer "nothing left" for rows that are merely invisible to whoever this
/// scope thinks is asking — which is the shape of answer that produced the defect in the first
/// place.
/// </para>
/// <para>
/// <b>It carries exactly ONE exemption, and naming it here is part of the exemption.</b> A row of
/// the Backups module's <see cref="Backup"/> entity that <see cref="Backup.SurvivesAccountDeletion"/>
/// claims — the §12 final backup, taken immediately before this very deletion — is not counted as
/// residue, because it is meant to outlive the account and an audit with no exemption would refuse
/// every deletion that feature exists to protect. Everything about how narrow it is, and why it is
/// typed rather than name-matched, is on <c>CountSurvivingBackupsAsync</c> below. It is the only
/// place this class knows a module by name, and it should stay the only one: an exemption per
/// module is a list, and a list is the thing this auditor was written instead of.
/// </para>
/// <para>
/// <b>Its blind spot, stated rather than papered over.</b> A context that cannot be resolved or
/// whose table cannot be read is SKIPPED, because a scan that aborted a deletion over its own
/// failure would be worse than the leak it hunts — an account that cannot be deleted at all. So this
/// auditor is blind on the axis "the audit itself broke", and sighted on the axis that actually went
/// blind: a module that holds a customer's rows and released none of them.
/// </para>
/// <para>
/// <b>And a skip is RETURNED, not merely logged.</b> A log line is read by whoever goes looking; the
/// operator reading a finished deletion is not looking, because the panel has told them it finished.
/// So every skipped context comes back in <see cref="AccountResidue.Unchecked"/> and lands in the
/// task's own log beside the claim it qualifies. The log line is kept as well, at warning, for the
/// exception behind the skip, which has no place in an operator-facing summary.
/// </para>
/// </remarks>
public sealed class ModuleAccountResidueAuditor : IAccountResidueAuditor
{
    /// <summary>The property whose presence makes a row a customer's row.</summary>
    private const string TenantProperty = "AccountId";

    /// <summary>Pre-compiled log delegate for a module whose rows could not be audited.</summary>
    /// <remarks>
    /// Warning and not Error: the deletion is unaffected, and the operator's interest is that one
    /// module's rows went unchecked rather than that anything is broken. The module is named, so
    /// "unchecked" is never mistaken for "clean".
    /// </remarks>
    private static readonly Action<ILogger, string, Exception?> LogModuleUnaudited =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(ModuleAccountResidueAuditor)),
            "The rows of {Context} could not be audited for a deleted account; they were NOT checked.");

    /// <summary>The generic counter this class invokes once per mapped tenant entity.</summary>
    private static readonly MethodInfo CountMethod =
        typeof(ModuleAccountResidueAuditor).GetMethod(nameof(CountAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>The scope the module contexts are resolved from — the caller's own.</summary>
    private readonly IServiceProvider _services;

    /// <summary>Where a module that could not be audited is recorded.</summary>
    private readonly ILogger<ModuleAccountResidueAuditor> _logger;

    /// <summary>Creates the auditor.</summary>
    /// <param name="services">The scope the module contexts are resolved from.</param>
    /// <param name="logger">Where a module that could not be audited is recorded.</param>
    public ModuleAccountResidueAuditor(
        IServiceProvider services,
        ILogger<ModuleAccountResidueAuditor> logger)
    {
        _services = services;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AccountResidue> FindResidueAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var residue = new List<string>();
        var skipped = new List<string>();

        foreach (var contextType in ContextTypes())
        {
            try
            {
                if (_services.GetService(contextType) is not DbContext context)
                {
                    // Not a skip: a context this panel does not compose holds nothing to skip over.
                    continue;
                }

                foreach (var entity in TenantEntities(context))
                {
                    var count = entity.ClrType == typeof(Backup)
                        ? await CountSurvivingBackupsAsync(context, accountId, cancellationToken)
                        : await CountRowsAsync(context, entity, accountId, cancellationToken);

                    if (count > 0)
                    {
                        residue.Add($"{entity.ClrType.Name}({count})");
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Deliberately broad, with the reason on the line: the audit must not be the thing
                // that makes an account undeletable. Every kind of failure here — a schema that is
                // not migrated, a provider that refused the model, a context this scope cannot
                // build — has the same meaning for the caller, and none of them is a leak.
                LogModuleUnaudited(_logger, contextType.Name, exception);
                skipped.Add(contextType.Name);
            }
        }

        residue.Sort(StringComparer.Ordinal);
        skipped.Sort(StringComparer.Ordinal);

        return new AccountResidue(residue, skipped);
    }

    /// <summary>Every <c>DbContext</c> type the composed modules declare.</summary>
    /// <returns>The context types, in a stable order.</returns>
    private static IEnumerable<Type> ContextTypes()
    {
        return ModuleRegistry.All
            .Select(module => { return module.GetType().Assembly; })
            .Distinct()
            .SelectMany(assembly => { return assembly.GetTypes(); })
            .Where(type => { return type.IsSubclassOf(typeof(DbContext)) && !type.IsAbstract; })
            .OrderBy(type => { return type.FullName; }, StringComparer.Ordinal);
    }

    /// <summary>The context's mapped entities that carry an account id.</summary>
    /// <param name="context">The module context whose model is read.</param>
    /// <returns>The tenant entity types.</returns>
    private static IEnumerable<IEntityType> TenantEntities(DbContext context)
    {
        return context.Model.GetEntityTypes()
            .Where(entity => { return entity.FindProperty(TenantProperty) is not null; });
    }

    /// <summary>Counts one entity's surviving rows for the account.</summary>
    /// <param name="context">The module context to query.</param>
    /// <param name="entity">The mapped tenant entity.</param>
    /// <param name="accountId">The account being deleted.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>How many rows survive.</returns>
    private static Task<int> CountRowsAsync(
        DbContext context,
        IEntityType entity,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        return (Task<int>)CountMethod
            .MakeGenericMethod(entity.ClrType)
            .Invoke(null, [context, accountId, cancellationToken])!;
    }

    /// <summary>
    /// Counts the backup rows that still name the account and are NOT the final backup taken for
    /// this deletion.
    /// </summary>
    /// <param name="context">The Backups module's context.</param>
    /// <param name="accountId">The account being deleted.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>How many backup rows count as residue.</returns>
    /// <remarks>
    /// <para>
    /// <b>This is the one exemption in this auditor, and it is a hole in the check that caught the
    /// cascade defect — so read it as one.</b> Added 2026-09-07 for the plan's Task 13. Everywhere
    /// else, a row still naming a deleted account refuses the deletion; here, exactly one kind of
    /// row does not, because that row is the whole point of the operation. The §12 final backup is
    /// taken immediately before the account is destroyed and must outlive it, so a residue audit
    /// with no exemption would refuse every deletion the final backup exists to protect — the check
    /// and the feature would cancel each other out and the visible symptom would be "accounts can no
    /// longer be deleted".
    /// </para>
    /// <para>
    /// <b>Why it is narrowed twice over rather than once.</b> It is keyed on the CLR type
    /// <see cref="Backup"/>, so no other module can fall into it whatever it names its rows; and
    /// within that type it is keyed on <see cref="Backup.SurvivesAccountDeletion"/>, the same
    /// predicate the Backups module's own cascade handler applies, so the audit and the cascade
    /// cannot disagree about which row was meant to be kept. A backup of any other kind that
    /// survived the cascade is still residue and still refuses the deletion, which is what
    /// <c>DeleteAccountResidueTests</c> pins.
    /// </para>
    /// <para>
    /// <b>The typed reference is the point, not an accident of the Host referencing the module.</b>
    /// A name-matched exemption (<c>"Backup"</c>, <c>"PreDeletion"</c>) would survive a rename and
    /// silently stop applying — or start applying to somebody else's <c>Backup</c>. The compiler is
    /// the thing that notices, so the compiler is what this is written against. This does not let
    /// the Host know a module's business in general: it composes them already, and what it knows
    /// here is one entity and one predicate that entity itself declares.
    /// </para>
    /// </remarks>
    private static async Task<int> CountSurvivingBackupsAsync(
        DbContext context,
        Guid accountId,
        CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // the account is being deleted; its rows must be found whoever asked
        var owned = await context.Set<Backup>()
            .IgnoreQueryFilters()
            .Where(row => row.AccountId == accountId)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030

        // Counted in memory so the exemption is the ENTITY'S predicate and not a translation of it.
        // A `Kind != PreDeletion` in the SQL would be a second spelling of the rule, and the two
        // could drift apart without either side failing to compile. Materialising is affordable
        // here: this runs once per account deletion, over one account's backups.
        return owned.Count(row => { return !row.SurvivesAccountDeletion(); });
    }

    /// <summary>Counts the rows of one mapped entity that still name the account.</summary>
    /// <typeparam name="TEntity">The mapped tenant entity.</typeparam>
    /// <param name="context">The module context to query.</param>
    /// <param name="accountId">The account being deleted.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>How many rows survive.</returns>
    /// <remarks>
    /// The account id is compared as a NULLABLE guid so that one expression serves both shapes of
    /// the column — <c>Site.AccountId</c> is a <c>Guid</c> and <c>User.AccountId</c> is a
    /// <c>Guid?</c>, and a null one matches no account, which is the right answer for the panel's
    /// administrator.
    /// </remarks>
    private static Task<int> CountAsync<TEntity>(
        DbContext context,
        Guid accountId,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        Expression<Func<TEntity, bool>> owned = row =>
            EF.Property<Guid?>(row, TenantProperty) == accountId;

#pragma warning disable RS0030 // the account is being deleted; its rows must be found whoever asked
        return context.Set<TEntity>().IgnoreQueryFilters().CountAsync(owned, cancellationToken);
#pragma warning restore RS0030
    }
}
