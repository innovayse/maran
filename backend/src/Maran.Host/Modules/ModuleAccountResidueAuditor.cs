using System.Linq.Expressions;
using System.Reflection;
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
/// entities carrying an <c>AccountId</c>, exactly as <c>TenantScopeTests</c> and
/// <c>AccountCascadeTests</c> do, so a module added later — or a marketplace module this assembly
/// was never compiled knowing about — is audited without anybody extending anything. The one thing
/// a maintained list would add is the ability to forget a module, which is the defect this exists
/// for.
/// </para>
/// <para>
/// <b>The query filters are bypassed, deliberately.</b> A filter governs what a REQUEST may see, and
/// this is not a request for rows: it is the audit of an account already authorised for deletion. A
/// filtered count would answer "nothing left" for rows that are merely invisible to whoever this
/// scope thinks is asking — which is the shape of answer that produced the defect in the first
/// place.
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
                    var count = await CountRowsAsync(context, entity, accountId, cancellationToken);
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
