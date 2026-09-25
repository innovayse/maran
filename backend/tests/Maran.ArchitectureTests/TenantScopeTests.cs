using Maran.ArchitectureTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Maran.ArchitectureTests;

/// <summary>
/// Makes tenant scoping a property of the build rather than of a reviewer's attention: an entity that
/// is a customer's row — because it carries an <c>AccountId</c>, or because it exists only as a
/// required child of something that does — is separated from another customer's rows by a global
/// query filter or by nothing at all.
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
/// <b>The criterion is "belongs to a tenant", not "spells it AccountId".</b> It was the latter, and
/// that is narrower than the guarantee this class's name claims: <c>SiteHostname</c> is scoped
/// through its site's account and carries no <c>AccountId</c> at all, so it was filtered OUTSIDE the
/// reach of the test that exists to notice a missing filter. <see cref="TenantScopeCensus"/> now
/// follows required relationships as well, and states what that traversal still cannot see.
/// </para>
/// <para>
/// <b>Exemptions are named, reasoned and checked for staleness.</b> The entities here that are a
/// customer's rows and must NOT be filtered are Identity's, and the reason is written beside them.
/// An exemption for an entity that no longer exists fails
/// <see cref="Every_exemption_still_names_a_real_entity"/>, so the list cannot rot into a place
/// where a real tenant table hides.
/// </para>
/// <para>
/// <b>The positive control asserts a derived VALUE, not a floor.</b> It used to require "at least
/// eight" contexts of twelve and "at least five" tenant entities of eight, so a third of the model
/// could stop being walked while the guard above went quieter without going red — the vacuity trap,
/// inside the check meant to catch it. Both expectations are now derived from the build graph
/// (<see cref="TenantScopeCensus"/>), which is a different source from the registry the walk reads,
/// and an empty derivation fails rather than agreeing with an empty walk.
/// </para>
/// </remarks>
public sealed class TenantScopeTests
{
    /// <summary>
    /// Entities that are a customer's rows and are deliberately not tenant-filtered, each with the
    /// reason it is safe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All five are Identity's, and they are one decision rather than five. A filter closes over the
    /// authenticated principal, and every query here runs when there is NO principal yet: sign-in
    /// looks a user up by e-mail and then a session up by token hash, a password reset is consumed by
    /// a caller holding nothing but the token, an invitation is consumed the same way by a caller
    /// holding nothing but ITS token, and a recovery code is consumed part-way through two-factor,
    /// before a session exists. A filtered table would find nobody and refuse every login on the
    /// panel. <c>User.AccountId</c> is nullable precisely because an administrator has none; the
    /// other four reach a tenant only THROUGH that user, which is why they appear here only now that
    /// the census follows relationships.
    /// </para>
    /// <para>
    /// <c>InvitationToken</c> qualifies for the identical reason <c>PasswordResetToken</c> does: it
    /// is reached by an anonymous caller presenting a bearer token — the plaintext value mailed to a
    /// hosting account's owner — before any principal exists for a filter to close over. Nothing
    /// exposes it by id; the only lookup is by the token's own digest.
    /// </para>
    /// <para>
    /// What makes the absence safe is that nothing exposes these rows by id — Identity's HTTP surface
    /// is sign-in, sessions, two-factor, password reset, the setup flow, and two administrator-only
    /// screens, and a session is addressed by its own id only for the signed-in principal that owns
    /// it. Adding an endpoint that reads one of these rows by an id the caller names is what would
    /// change that, and it must scope the read itself.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> Exempt =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Maran.Modules.Identity.Domain.Entities.User"] =
                "read by sign-in before any principal exists; no endpoint reads a user the caller names",
            ["Maran.Modules.Identity.Domain.Entities.Session"] =
                "looked up by token hash to ESTABLISH the principal a filter would close over",
            ["Maran.Modules.Identity.Domain.Entities.PasswordResetToken"] =
                "consumed by an unauthenticated caller holding the token; there is no principal to scope to",
            ["Maran.Modules.Identity.Domain.Entities.InvitationToken"] =
                "consumed by an unauthenticated caller holding the token; there is no principal to scope to",
            ["Maran.Modules.Identity.Domain.Entities.RecoveryCode"] =
                "consumed part-way through two-factor, before the session that would carry a principal exists",
        };

    /// <summary>Every entity that belongs to a tenant is scoped by a global query filter.</summary>
    [Fact]
    public void Every_tenant_entity_is_scoped_by_a_query_filter()
    {
        var contexts = ModuleDbContexts.CreateAll();
        try
        {
            var direct = Unscoped(TenantScopeCensus.CarryingTenantProperty(contexts));
            var related = Unscoped(TenantScopeCensus.ScopedThroughRelationship(contexts));

            Assert.True(
                direct.Count == 0,
                $"These entities carry an {TenantScopeCensus.TenantProperty} and no global query filter, "
                + "so one customer's rows are separated from another's only by whatever each handler "
                + $"remembers to write: {string.Join(", ", direct)}. Register a filter in the module's "
                + "DbContext, or add the entity to TenantScopeTests.Exempt with the reason it is safe.");

            Assert.True(
                related.Count == 0,
                "These entities exist only as a REQUIRED child of a customer's row, so they are that "
                + "customer's rows too, and they have no global query filter: "
                + $"{string.Join(", ", related)}. Filter them through the relationship — "
                + "`HasQueryFilter(child => admin || child.Parent.AccountId == …)` — or add the entity "
                + "to TenantScopeTests.Exempt with the reason it is safe.");
        }
        finally
        {
            Dispose(contexts);
        }
    }

    /// <summary>Every exemption still names an entity the census can see.</summary>
    /// <remarks>
    /// The staleness guard. An exemption for a renamed or deleted entity is dead text that reads
    /// like a decision, and the next entity to take that name would inherit an exemption nobody
    /// granted it.
    /// </remarks>
    [Fact]
    public void Every_exemption_still_names_a_real_entity()
    {
        var contexts = ModuleDbContexts.CreateAll();
        try
        {
            var present = TenantScopeCensus.TenantEntities(contexts)
                .Select(TenantScopeCensus.NameOf)
                .ToHashSet(StringComparer.Ordinal);

            var stale = Exempt.Keys
                .Where(name =>
                {
                    return !present.Contains(name);
                })
                .OrderBy(name =>
                {
                    return name;
                }, StringComparer.Ordinal)
                .ToList();

            Assert.True(
                stale.Count == 0,
                "Exemptions for entities the tenant census no longer sees — they were renamed, deleted, "
                + $"or stopped belonging to a tenant: {string.Join(", ", stale)}");
        }
        finally
        {
            Dispose(contexts);
        }
    }

    /// <summary>The census walks every DbContext compiled into a module, not merely several of them.</summary>
    /// <remarks>
    /// Half of the positive control, and the half the old floor could not express. The walk reads
    /// <c>ModuleRegistry.All</c>; this expectation reads the module assemblies that shipped into the
    /// test run. A context that stops being composed — a registry entry deleted, a module whose
    /// registration was lost in a merge — is NAMED here, where "at least eight of twelve" said
    /// nothing about four of them.
    /// </remarks>
    [Fact]
    public void Census_walks_every_DbContext_compiled_into_a_module()
    {
        var expected = TenantScopeCensus.ContextTypesCompiledIntoModules();

        Assert.True(
            expected.Count > 0,
            "No module DbContext type was found in any Maran.Modules.* assembly beside the test binary, "
            + "so this control derived its expectation from nothing and would agree with a walk that "
            + "found nothing. Something is wrong with ModuleAssemblies, not with the panel.");

        var contexts = ModuleDbContexts.CreateAll();
        try
        {
            var walked = contexts
                .Select(context =>
                {
                    return context.GetType();
                })
                .ToHashSet();

            var missing = expected
                .Where(type =>
                {
                    return !walked.Contains(type);
                })
                .Select(type =>
                {
                    return type.FullName ?? type.Name;
                })
                .OrderBy(name =>
                {
                    return name;
                }, StringComparer.Ordinal)
                .ToList();

            Assert.True(
                missing.Count == 0,
                $"{expected.Count} module DbContexts are compiled into this panel and the tenant census "
                + $"built {walked.Count}. These were never walked, so nothing above says anything about "
                + $"their tenancy: {string.Join(", ", missing)}. Either the module is missing from "
                + "ModuleRegistry.All, or ModuleDbContexts could not construct it.");
        }
        finally
        {
            Dispose(contexts);
        }
    }

    /// <summary>The census finds every entity type that declares an account id.</summary>
    /// <remarks>
    /// The other half. The expectation is reflection over the module assemblies' entity types, which
    /// is a different source from the models the walk reads, so an entity that stops being MAPPED —
    /// a <c>DbSet</c> removed, a configuration dropped, a context that failed to build — is named
    /// instead of quietly shrinking the set the filter test iterates.
    /// </remarks>
    [Fact]
    public void Census_finds_every_entity_type_that_declares_an_account_id()
    {
        var expected = TenantScopeCensus.EntityTypesDeclaringTenantProperty();

        Assert.True(
            expected.Count > 0,
            $"No entity type declaring an {TenantScopeCensus.TenantProperty} was found in any "
            + "Maran.Modules.* assembly beside the test binary, so this control derived its expectation "
            + "from nothing and would agree with a census that found nothing.");

        var contexts = ModuleDbContexts.CreateAll();
        try
        {
            var census = TenantScopeCensus.CarryingTenantProperty(contexts)
                .Select(entity =>
                {
                    return entity.ClrType;
                })
                .ToHashSet();

            var unseen = expected
                .Where(type =>
                {
                    return !census.Contains(type);
                })
                .Select(type =>
                {
                    return type.FullName ?? type.Name;
                })
                .OrderBy(name =>
                {
                    return name;
                }, StringComparer.Ordinal)
                .ToList();

            Assert.True(
                unseen.Count == 0,
                $"These entity types declare an {TenantScopeCensus.TenantProperty} and the tenant census "
                + $"did not see them, so their filter is not checked by anything: "
                + $"{string.Join(", ", unseen)}. The census saw {census.Count} of {expected.Count}.");
        }
        finally
        {
            Dispose(contexts);
        }
    }

    /// <summary>The entities of a census set that carry no query filter and no exemption.</summary>
    /// <param name="entities">The census set to sift.</param>
    /// <returns>Their names, ordered, with the exempted ones removed.</returns>
    private static List<string> Unscoped(IReadOnlyList<IEntityType> entities)
    {
        return entities
            .Where(entity =>
            {
                return entity.GetQueryFilter() is null;
            })
            .Select(TenantScopeCensus.NameOf)
            .Where(name =>
            {
                return !Exempt.ContainsKey(name);
            })
            .OrderBy(name =>
            {
                return name;
            }, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Releases the contexts a census walk built.</summary>
    /// <param name="contexts">The contexts to dispose.</param>
    private static void Dispose(IReadOnlyList<DbContext> contexts)
    {
        foreach (var context in contexts)
        {
            context.Dispose();
        }
    }
}
