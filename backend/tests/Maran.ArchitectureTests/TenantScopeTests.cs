using Maran.ArchitectureTests.Fixtures;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Maran.ArchitectureTests;

/// <summary>
/// Makes tenant scoping a property of the build rather than of a reviewer's attention: an entity
/// that carries an <c>AccountId</c> is a customer's row, and a customer's row is separated from
/// another customer's by a global query filter or by nothing at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is mechanical and the checklist was not.</b> rules/security.md item 6 says "new
/// tenant entity ⇒ registered in the filter fixture ⇒ IDOR test exists", and every module has
/// honoured it — by hand, four times, in four separate per-module test files. Nothing connected
/// them: a fifth module, or a third-party one, could add a tenant table and no test anywhere would
/// notice, because the rule lived in a document that only people read. This asks the MODEL, so it
/// covers a module written after it without being told about it.
/// </para>
/// <para>
/// <b>What a missing filter actually costs.</b> Without one, tenancy is enforced by every handler
/// remembering a <c>Where</c> clause, and the one that forgets does not fail — it succeeds, returning
/// another customer's row to a caller who guessed an id. That is the IDOR the panel answers 404 to
/// by construction, and the construction is the filter.
/// </para>
/// <para>
/// <b>Exemptions are named, reasoned and checked for staleness.</b> The one entity here that carries
/// an <c>AccountId</c> and must NOT be filtered is <c>User</c>, and the reason is written beside it.
/// An exemption for an entity that no longer exists fails
/// <see cref="Every_exemption_still_names_a_real_entity"/>, so the list cannot rot into a place
/// where a real tenant table hides.
/// </para>
/// </remarks>
public sealed class TenantScopeTests
{
    /// <summary>The property whose presence makes a row a customer's row.</summary>
    private const string TenantProperty = "AccountId";

    /// <summary>
    /// Entities that carry <see cref="TenantProperty"/> and are deliberately not tenant-filtered,
    /// each with the reason it is safe.
    /// </summary>
    /// <remarks>
    /// <c>User</c> is the whole list, and it is not an oversight. A filter closes over the
    /// authenticated principal, and the queries that read this table run when there is NO principal
    /// yet: sign-in looks a user up by e-mail before anyone is authenticated, and a filtered table
    /// would find nobody and refuse every login on the panel. Its <c>AccountId</c> is nullable
    /// precisely because an administrator has none. What makes the absence safe is that nothing
    /// exposes a user by id or lists users at all — Identity's HTTP surface is sign-in, sessions,
    /// two-factor, password reset, the setup flow, and two administrator-only screens — so there is
    /// no request whose answer a filter would have narrowed. Adding an endpoint that reads a user
    /// the caller names is what would change that, and it must scope the read itself.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> Exempt =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Maran.Modules.Identity.Domain.Entities.User"] =
                "read by sign-in before any principal exists; no endpoint reads a user the caller names",
        };

    /// <summary>Every entity carrying an account id is scoped by a global query filter.</summary>
    [Fact]
    public void Every_tenant_entity_is_scoped_by_a_query_filter()
    {
        var unscoped = TenantEntities()
            .Where(entity => { return entity.GetQueryFilter() is null; })
            .Select(entity => { return entity.ClrType.FullName ?? entity.Name; })
            .Where(name => { return !Exempt.ContainsKey(name); })
            .OrderBy(name => { return name; }, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unscoped.Count == 0,
            $"These entities carry an {TenantProperty} and no global query filter, so one customer's "
            + $"rows are separated from another's only by whatever each handler remembers to write: "
            + $"{string.Join(", ", unscoped)}. Register a filter in the module's DbContext, or add the "
            + "entity to TenantScopeTests.Exempt with the reason it is safe.");
    }

    /// <summary>Every exemption still names an entity that exists and still carries an account id.</summary>
    /// <remarks>
    /// The staleness guard. An exemption for a renamed or deleted entity is dead text that reads
    /// like a decision, and the next entity to take that name would inherit an exemption nobody
    /// granted it.
    /// </remarks>
    [Fact]
    public void Every_exemption_still_names_a_real_entity()
    {
        var present = TenantEntities()
            .Select(entity => { return entity.ClrType.FullName ?? entity.Name; })
            .ToHashSet(StringComparer.Ordinal);

        var stale = Exempt.Keys
            .Where(name => { return !present.Contains(name); })
            .OrderBy(name => { return name; }, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            stale.Count == 0,
            $"Exemptions for entities that no longer carry an {TenantProperty}: {string.Join(", ", stale)}");
    }

    /// <summary>The census reads real models rather than finding nothing.</summary>
    /// <remarks>
    /// The positive control, and it guards the axis that goes blind. Every assertion above passes
    /// trivially when no context could be built or no entity was discovered — a renamed property, a
    /// registry that stopped listing modules, a provider that refused the model. So the census is
    /// required to have found several contexts, a working number of tenant entities, and two
    /// specific ones planted here as the value the probe must find.
    /// </remarks>
    [Fact]
    public void Census_reads_the_models_of_the_composed_modules()
    {
        var contexts = ModuleDbContexts.CreateAll();
        var tenantEntities = TenantEntities()
            .Select(entity => { return entity.ClrType.Name; })
            .ToList();

        Assert.True(contexts.Count >= 8, $"Only {contexts.Count} module DbContexts were built");
        Assert.True(tenantEntities.Count >= 5, $"Only {tenantEntities.Count} tenant entities were found");
        Assert.Contains("Site", tenantEntities);
        Assert.Contains("Certificate", tenantEntities);

        foreach (var context in contexts)
        {
            context.Dispose();
        }
    }

    /// <summary>Every mapped entity, across every composed module, that carries an account id.</summary>
    /// <returns>The entity types whose model declares <see cref="TenantProperty"/>.</returns>
    private static List<IEntityType> TenantEntities()
    {
        var contexts = ModuleDbContexts.CreateAll();
        try
        {
            return contexts
                .SelectMany(context => { return context.Model.GetEntityTypes(); })
                .Where(entity => { return entity.FindProperty(TenantProperty) is not null; })
                .ToList();
        }
        finally
        {
            foreach (var context in contexts)
            {
                context.Dispose();
            }
        }
    }
}
