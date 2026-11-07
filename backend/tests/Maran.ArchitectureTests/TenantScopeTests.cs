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
/// <b>The reseller axis (issue #179).</b> What an account's rows are to <c>AccountId</c>, the accounts and
/// plans a reseller sells are to <c>ResellerId</c>: an entity that carries it, and the entity it points at,
/// need a query filter too, or one reseller is separated from another only by a <c>Where</c> somebody
/// remembered. <see cref="TenantScopeCensus"/> reads both; the real walk is held by a second source
/// (the entity types declaring the property) and by a planted unfiltered entity it must refuse and a
/// planted filtered one it must accept.
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
    /// <c>AccountMembership</c> is Identity's one entry and carries an <c>AccountId</c>, and it is exempt
    /// on purpose: it is the table that DECIDES which account a session may act for, so it is read by
    /// user id across every account a login holds, at sign-in (before a principal exists) and when the
    /// person switches account. Filtered by the account claim, it would show only the account already
    /// active and make a switch impossible. The endpoints that read it stamp the user id from the token;
    /// none takes a membership id. <c>AccountMembershipOffer</c> is exempt for the same reason from the
    /// other side: it is read by the person an account's owner offered a place to, whose session speaks
    /// for a different account or for none.
    /// </para>
    /// <para>
    /// <b>What is no longer listed, and why that is not a loss of coverage.</b> <c>User</c>,
    /// <c>Session</c>, <c>PasswordResetToken</c>, <c>InvitationToken</c>, <c>RecoveryCode</c> and
    /// <c>PinnedPage</c> used to be exempt here because the census reached them through <c>User.AccountId</c>
    /// and the required relationships hanging off it. A login names no account any more (issue #325; the legacy column is a shadow property the census
    /// does not walk): it
    /// belongs to a PERSON, who may hold several accounts or, for an administrator, none, so these rows are
    /// not an account's rows and the census correctly stops seeing them. What was true of them is
    /// unchanged — they are read at sign-in or by an unauthenticated caller holding a token, before any
    /// principal exists, and nothing exposes one by an id the caller names — and an endpoint that did
    /// would have to scope the read itself.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> Exempt =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Maran.Modules.Identity.Domain.Entities.AccountMembership"] =
                "read by the login's own user id across ALL its accounts, to decide which one a session may act for; a tenant filter would hide every account but the active one",
            ["Maran.Modules.Identity.Domain.Entities.AccountMembershipOffer"] =
                "the offered login's row as much as the account's: the invitee reads, accepts and declines it from a session that speaks for another account or none, so an account filter would hide it from them; every read compares the caller's own user id, and the owner's reads are scoped to the account their token names",
            ["Maran.Modules.Identity.Domain.Entities.AuditEvent"] =
                "an administrator-only, server-wide journal that carries a nullable AccountId only so the administrator's account filter, export and erasure can find an account's entries; it is also written with no principal at all (a failed sign-in), so a filter keyed on the caller's account would hide those rows from the writer. It is read only through controllers behind AdminOnly and no customer endpoint lists it. A customer-facing view of the journal would NOT be safe under this exemption: it would need a query filter on AccountId first, and this entry removed",
            ["Maran.Modules.Identity.Domain.Entities.User"] =
                "carries the reseller id of a RESELLER LOGIN, which is what a token's reseller claim is minted from; it is read at sign-in, before any principal exists, and by the login's own user id, so a filter keyed on the caller's reseller would hide the row from the very person signing in. Reseller isolation of the data a reseller sells is the Accounts filters' job, not this table's",
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

    /// <summary>Every entity a reseller owns, and the owner they point at, is scoped by a global query filter.</summary>
    [Fact]
    public void Every_reseller_owned_entity_is_scoped_by_a_query_filter()
    {
        var contexts = ModuleDbContexts.CreateAll();
        try
        {
            var owned = Unscoped(TenantScopeCensus.CarryingOwnerProperty(contexts));
            var owners = Unscoped(TenantScopeCensus.OwnersOfRows(contexts));

            Assert.True(
                owned.Count == 0,
                $"These entities carry a {TenantScopeCensus.OwnerProperty} and no global query filter, so one "
                + "reseller's rows are separated from another's only by whatever each handler remembers to "
                + $"write: {string.Join(", ", owned)}. Register a filter in the module's DbContext, or add "
                + "the entity to TenantScopeTests.Exempt with the reason it is safe.");

            Assert.True(
                owners.Count == 0,
                "These entities are the owner a reseller-owned row points at, and they have no global query "
                + $"filter, so one reseller can read another's own row: {string.Join(", ", owners)}. Filter "
                + "them on the caller's reseller, or add the entity to TenantScopeTests.Exempt with the "
                + "reason it is safe.");
        }
        finally
        {
            Dispose(contexts);
        }
    }

    /// <summary>The census finds every entity type that declares a reseller id, and the owner they point at.</summary>
    /// <remarks>
    /// The second source for the reseller axis, as the account axis has: reflection over the module
    /// assemblies' entity types, a different source from the models the walk reads. The owner control
    /// shows the foreign-key walk reaches the entity the owned rows belong to; without it the owner
    /// check above is satisfied by a census that never finds one.
    /// </remarks>
    [Fact]
    public void Census_finds_every_entity_type_that_declares_a_reseller_id_and_their_owner()
    {
        var expected = TenantScopeCensus.EntityTypesDeclaringOwnerProperty();

        Assert.True(
            expected.Count > 0,
            $"No entity type declaring a {TenantScopeCensus.OwnerProperty} was found in any Maran.Modules.* "
            + "assembly beside the test binary, so this control derived its expectation from nothing and "
            + "would agree with a census that found nothing.");

        var contexts = ModuleDbContexts.CreateAll();
        try
        {
            var census = TenantScopeCensus.CarryingOwnerProperty(contexts)
                .Select(entity => { return entity.ClrType; })
                .ToHashSet();

            var unseen = expected
                .Where(type => { return !census.Contains(type); })
                .Select(type => { return type.FullName ?? type.Name; })
                .OrderBy(name => { return name; }, StringComparer.Ordinal)
                .ToList();

            Assert.True(
                unseen.Count == 0,
                $"These entity types declare a {TenantScopeCensus.OwnerProperty} and the tenant census did "
                + $"not see them, so their filter is not checked by anything: {string.Join(", ", unseen)}. "
                + $"The census saw {census.Count} of {expected.Count}.");

            Assert.NotEmpty(TenantScopeCensus.OwnersOfRows(contexts));
        }
        finally
        {
            Dispose(contexts);
        }
    }

    /// <summary>The reseller axis names an unfiltered entity and accepts a filtered one.</summary>
    /// <remarks>
    /// The positive and refusing controls in one planted model. The census must SEE both entities
    /// (or the refusal below could be a census that finds nothing), name only the unfiltered one, and say
    /// nothing about the filtered one (or the refusal could be a census that names everything).
    /// </remarks>
    [Fact]
    public void The_reseller_axis_names_a_planted_unfiltered_entity_and_accepts_a_planted_filtered_one()
    {
        var options = new DbContextOptionsBuilder<PlantedResellerContext>()
            .UseNpgsql("Host=planted.invalid;Database=maran")
            .Options;
        using var context = new PlantedResellerContext(options);
        var contexts = new List<DbContext> { context };

        var seen = TenantScopeCensus.CarryingOwnerProperty(contexts)
            .Select(TenantScopeCensus.NameOf)
            .OrderBy(name => { return name; }, StringComparer.Ordinal)
            .ToList();
        var unscoped = Unscoped(TenantScopeCensus.CarryingOwnerProperty(contexts));

        Assert.Equal(
            new[] { typeof(PlantedFilteredResellerRow).FullName!, typeof(PlantedUnfilteredResellerRow).FullName! },
            seen);
        Assert.Equal(new[] { typeof(PlantedUnfilteredResellerRow).FullName! }, unscoped);
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
