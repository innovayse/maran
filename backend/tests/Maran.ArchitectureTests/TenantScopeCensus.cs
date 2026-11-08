using System.Reflection;
using Maran.ArchitectureTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Maran.ArchitectureTests;

/// <summary>
/// Answers "which mapped entities are a customer's rows" — the question <see cref="TenantScopeTests"/>
/// asks of the model — and derives, from a SECOND source, what that answer is supposed to contain.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two ways to belong to a tenant, and the guard used to see one.</b> An entity that carries an
/// <c>AccountId</c> is a customer's row. So is an entity that exists only as a REQUIRED child of one:
/// <c>SiteHostname</c> belongs to whoever owns the site claiming it and deliberately carries no
/// second copy of the account id, because a second copy can disagree with the site's own. "Has an
/// <c>AccountId</c>" is therefore a proxy for "belongs to a tenant", and a proxy silently stops
/// seeing the first thing spelled differently. This class widens the criterion to the relationship
/// that makes the child a tenant's row — a required foreign key to a tenant entity, transitively —
/// so a child entity added tomorrow with no filter fails a test rather than joining a blind spot.
/// </para>
/// <para>
/// <b>Why a traversal rather than a register of names.</b> A register would have to be extended by
/// the same person who forgot the filter, and the whole point of asking the model is that it cannot
/// be forgotten. The register this repository still keeps is the small one that carries DECISIONS —
/// <c>TenantScopeTests.Exempt</c>, an entity that must not be filtered plus the reason it is safe —
/// and that one has a staleness guard because a decision can rot while a derivation cannot.
/// </para>
/// <para>
/// <b>What the traversal cannot see, stated rather than implied.</b> An OPTIONAL foreign key is not
/// followed: a row that may exist with no owner is not the owner's row, and following it would flag
/// shared reference tables (<c>BackupDestination</c>) as tenant data. An entity reachable only
/// through code — a handler that joins two tables the model does not relate — is invisible here, and
/// so is a tenant column spelled as anything but <c>AccountId</c> on an entity with no relationship
/// to one. Owned types are skipped because they are queried through their owner and cannot carry a
/// filter of their own; their owner's filter is what governs them.
/// </para>
/// <para>
/// <b>A second axis: whose rows a RESELLER sees (issue #179).</b> An account's rows are separated by
/// <c>AccountId</c>; the accounts and plans a reseller sells are separated by <c>ResellerId</c>, and
/// the same blindness would follow: a table that carries it and no filter is separated from another
/// reseller's only by whatever each handler remembers to write. <see cref="CarryingOwnerProperty"/>
/// finds the entities that carry it, and <see cref="OwnersOfRows"/> finds the entity those rows
/// belong to (the principal of their <c>ResellerId</c> foreign key — the reseller itself, whose own
/// row is the one a reseller may read). The reseller foreign key is OPTIONAL (null means the
/// administrator's), so <see cref="ScopedThroughRelationship(IReadOnlyList{DbContext})"/> does not
/// follow it and a required child of a reseller's row is not seen: stated here, not implied.
/// </para>
/// <para>
/// <b>The second source, and why a floor is not a control.</b> <see cref="ModuleDbContexts"/> walks
/// the contexts <c>ModuleRegistry.All</c> declares — a hand-written list. Asserting that the walk
/// found "at least eight" of twelve says nothing when four stop being composed, which is the failure
/// a positive control exists to catch. So the expectation is DERIVED instead, from the build graph:
/// every non-abstract <c>DbContext</c> compiled into a module assembly
/// (<see cref="ContextTypesCompiledIntoModules"/>), and every entity CLR type that declares the
/// tenant property (<see cref="EntityTypesDeclaringTenantProperty"/>). Both sides grow together when
/// a thirteenth module lands, so there is no number for a contributor to raise without thinking.
/// </para>
/// </remarks>
public static class TenantScopeCensus
{
    /// <summary>The property whose presence makes a row a customer's row.</summary>
    public const string TenantProperty = "AccountId";

    /// <summary>The property whose presence makes a row one a reseller owns.</summary>
    public const string OwnerProperty = "ResellerId";

    /// <summary>The namespace suffix the backend layout gives every mapped entity.</summary>
    /// <remarks>
    /// rules/csharp.md files entities under <c>&lt;Module&gt;/Domain/Entities/</c> and
    /// <c>maran structure</c> enforces the map, so the namespace is a reliable way to reach entity
    /// types by reflection without loading a model first — which is the point: this side of the
    /// comparison must not be derived from the model it is checking.
    /// </remarks>
    private const string EntityNamespaceSuffix = ".Domain.Entities";

    /// <summary>Every <c>DbContext</c> compiled into a module assembly, whatever the registry says.</summary>
    /// <returns>The context types, in a stable order.</returns>
    public static List<Type> ContextTypesCompiledIntoModules()
    {
        return ModuleAssemblies.Modules()
            .SelectMany(assembly =>
            {
                return assembly.GetTypes();
            })
            .Where(type =>
            {
                return type.IsSubclassOf(typeof(DbContext)) && !type.IsAbstract;
            })
            .OrderBy(type =>
            {
                return type.FullName;
            }, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Every <c>DbContext</c> compiled into a module assembly the panel composes: the contexts of
    /// <see cref="ContextTypesCompiledIntoModules"/> except those of the modules
    /// <see cref="ModuleContractCensus.UnregisteredModules"/> names.
    /// </summary>
    /// <remarks>
    /// The one list of modules built but not yet composed is <see cref="ModuleContractCensus.UnregisteredModules"/>;
    /// this method subtracts it rather than keeping a second list, so the day a module is composed and leaves
    /// that list its context is expected here, and walked, in the same change. The walk
    /// (<see cref="ModuleDbContexts"/>) reads <c>ModuleRegistry.All</c>, so a registered module whose context
    /// stopped being walked is still named by the comparison against this set.
    /// </remarks>
    /// <returns>The context types of the composed modules, in a stable order.</returns>
    public static List<Type> ContextTypesCompiledIntoRegisteredModules()
    {
        var unregistered = ModuleContractCensus.UnregisteredModules()
            .Select(module =>
            {
                return module.GetType().Assembly;
            })
            .ToHashSet();

        return ContextTypesCompiledIntoModules()
            .Where(type =>
            {
                return !unregistered.Contains(type.Assembly);
            })
            .ToList();
    }

    /// <summary>Every module entity CLR type that declares the tenant property.</summary>
    /// <returns>The entity types, in a stable order.</returns>
    public static List<Type> EntityTypesDeclaringTenantProperty()
    {
        return ModuleAssemblies.Modules()
            .SelectMany(assembly =>
            {
                return assembly.GetTypes();
            })
            .Where(type =>
            {
                return type.IsClass
                    && !type.IsAbstract
                    && type.Namespace?.EndsWith(EntityNamespaceSuffix, StringComparison.Ordinal) == true;
            })
            .Where(type =>
            {
                var property = type.GetProperty(
                    TenantProperty,
                    BindingFlags.Public | BindingFlags.Instance);
                return property is not null
                    && (property.PropertyType == typeof(Guid) || property.PropertyType == typeof(Guid?));
            })
            .OrderBy(type =>
            {
                return type.FullName;
            }, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The mapped entities that carry the tenant property themselves, owned types excepted.</summary>
    /// <remarks>An owned type's key is its owner's key, so an owned value of an account carries <c>AccountId</c> as a shadow foreign key; it is read only through its owner, whose filter scopes it, and EF refuses a filter on it.</remarks>
    /// <param name="contexts">The module models to read.</param>
    /// <returns>The tenant entity types.</returns>
    public static List<IEntityType> CarryingTenantProperty(IReadOnlyList<DbContext> contexts)
    {
        return contexts
            .SelectMany(context =>
            {
                return context.Model.GetEntityTypes();
            })
            .Where(entity =>
            {
                return !entity.IsOwned() && entity.FindProperty(TenantProperty) is not null;
            })
            .ToList();
    }

    /// <summary>
    /// The mapped entities that are a customer's rows because a REQUIRED relationship, followed
    /// transitively, reaches one that carries the tenant property.
    /// </summary>
    /// <param name="contexts">The module models to read.</param>
    /// <returns>The entity types scoped through a relationship, excluding those carrying the property.</returns>
    public static List<IEntityType> ScopedThroughRelationship(IReadOnlyList<DbContext> contexts)
    {
        var scoped = new List<IEntityType>();

        foreach (var context in contexts)
        {
            scoped.AddRange(ScopedThroughRelationship(context));
        }

        return scoped;
    }

    /// <summary>
    /// The mapped entities that carry the reseller-owner property themselves. An OWNED type is left out:
    /// it is stored with its owner, EF Core refuses a query filter on it, and the owner's filter is the
    /// one that hides it, so counting it would demand a filter that cannot exist.
    /// </summary>
    /// <param name="contexts">The module models to read.</param>
    /// <returns>The entity types a reseller may own rows of.</returns>
    public static List<IEntityType> CarryingOwnerProperty(IReadOnlyList<DbContext> contexts)
    {
        return contexts
            .SelectMany(context =>
            {
                return context.Model.GetEntityTypes();
            })
            .Where(entity =>
            {
                return !entity.IsOwned() && entity.FindProperty(OwnerProperty) is not null;
            })
            .ToList();
    }

    /// <summary>
    /// The mapped entities a reseller-owner property points at: the principal end of a foreign key
    /// whose dependent property is <see cref="OwnerProperty"/>.
    /// </summary>
    /// <param name="contexts">The module models to read.</param>
    /// <returns>The owner entity types, each once.</returns>
    public static List<IEntityType> OwnersOfRows(IReadOnlyList<DbContext> contexts)
    {
        return CarryingOwnerProperty(contexts)
            .SelectMany(entity =>
            {
                return entity.GetForeignKeys();
            })
            .Where(key =>
            {
                return key.Properties.Any(property => { return property.Name == OwnerProperty; });
            })
            .Select(key =>
            {
                return key.PrincipalEntityType;
            })
            .Distinct()
            .ToList();
    }

    /// <summary>Every module entity CLR type that declares the reseller-owner property.</summary>
    /// <returns>The entity types, in a stable order.</returns>
    public static List<Type> EntityTypesDeclaringOwnerProperty()
    {
        return ModuleAssemblies.Modules()
            .SelectMany(assembly =>
            {
                return assembly.GetTypes();
            })
            .Where(type =>
            {
                return type.IsClass
                    && !type.IsAbstract
                    && type.Namespace?.EndsWith(EntityNamespaceSuffix, StringComparison.Ordinal) == true;
            })
            .Where(type =>
            {
                var property = type.GetProperty(
                    OwnerProperty,
                    BindingFlags.Public | BindingFlags.Instance);
                return property is not null
                    && (property.PropertyType == typeof(Guid) || property.PropertyType == typeof(Guid?));
            })
            .OrderBy(type =>
            {
                return type.FullName;
            }, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Every entity this census treats as somebody's rows, by any criterion: carrying an account id,
    /// scoped through a required relationship, carrying a reseller owner, or being that owner.
    /// </summary>
    /// <param name="contexts">The module models to read.</param>
    /// <returns>The union, each entity once.</returns>
    public static List<IEntityType> TenantEntities(IReadOnlyList<DbContext> contexts)
    {
        var all = CarryingTenantProperty(contexts);
        all.AddRange(ScopedThroughRelationship(contexts));
        all.AddRange(CarryingOwnerProperty(contexts));
        all.AddRange(OwnersOfRows(contexts));
        return all.Distinct().ToList();
    }

    /// <summary>The full name a failure message names an entity by.</summary>
    /// <param name="entity">The mapped entity.</param>
    /// <returns>Its CLR full name, or the model's own name when the type is anonymous.</returns>
    public static string NameOf(IEntityType entity)
    {
        return entity.ClrType.FullName ?? entity.Name;
    }

    /// <summary>Closes one model over required relationships to its tenant entities.</summary>
    /// <param name="context">The module model to read.</param>
    /// <returns>The entities reached, excluding the tenant entities the walk started from.</returns>
    private static List<IEntityType> ScopedThroughRelationship(DbContext context)
    {
        var entities = context.Model.GetEntityTypes().ToList();
        var roots = entities
            .Where(entity =>
            {
                return entity.FindProperty(TenantProperty) is not null;
            })
            .ToHashSet();

        var reached = new HashSet<IEntityType>();
        var growing = true;

        while (growing)
        {
            growing = false;

            foreach (var entity in entities)
            {
                if (entity.IsOwned() || roots.Contains(entity) || reached.Contains(entity))
                {
                    continue;
                }

                var belongs = entity.GetForeignKeys().Any(key =>
                {
                    return key.IsRequired
                        && (roots.Contains(key.PrincipalEntityType) || reached.Contains(key.PrincipalEntityType));
                });

                if (belongs)
                {
                    reached.Add(entity);
                    growing = true;
                }
            }
        }

        return reached
            .OrderBy(NameOf, StringComparer.Ordinal)
            .ToList();
    }
}
