using System.Reflection;
using Maran.ArchitectureTests.Fixtures;
using Maran.Host.Modules;
using Maran.Sdk.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Maran.ArchitectureTests;

/// <summary>
/// Makes the account-deletion cascade a property of the build rather than of a reviewer's attention:
/// a module that owns rows keyed by <c>AccountId</c> subscribes to <see cref="AccountDeleting"/>, or
/// it is named here with the reason its rows may outlive the account.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is mechanical, and what it cost that it was not.</b> <see cref="AccountDeleting"/>'s
/// own remarks name the outcome it exists to prevent — "a row left behind is a customer's database
/// shown in a panel that no longer has an account for it" — and then name the two modules that hold
/// such rows. Two more did, and neither ever subscribed. A live browser run deleted an account that
/// owned a site and a certificate, watched the task report COMPLETED at 100, and then found the
/// <c>Site</c> row, the <c>Certificate</c> row, the nginx vhost and the account's <c>privkey.pem</c>
/// all still there — with the panel's sites screen listing an ENABLED site for an account that no
/// longer existed. Nothing in 2182 passing tests looked, because the one cascade integration test
/// gave its account no site and no certificate.
/// </para>
/// <para>
/// <b>It asks the MODEL, like <see cref="TenantScopeTests"/> does, and for the same reason.</b> The
/// question "does this module own a customer's rows" is answered by the mapping, not by a list
/// anybody maintains, so a module written after this test — or a marketplace module the open code
/// was never compiled knowing about — is covered without being told about it.
/// </para>
/// <para>
/// <b>What this test cannot see, said plainly.</b> It proves a subscriber EXISTS, not that it
/// removes everything, and it says nothing about resources that live outside the panel's database —
/// an account's crontab is on the host and in no table here. Those are the integration test's job
/// (<c>AccountDeletionCascadeTests</c>) and the polygon suite's
/// (<c>account_deletion_on_a_real_host.rs</c>). What this closes is the failure mode that actually
/// happened: nobody noticed a module had none.
/// </para>
/// </remarks>
public sealed class AccountCascadeTests
{
    /// <summary>The property whose presence makes a row a customer's row.</summary>
    private const string TenantProperty = "AccountId";

    /// <summary>
    /// Modules whose <see cref="TenantProperty"/>-carrying entities are deliberately NOT removed by
    /// the cascade, each with the reason keeping them is right.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It is empty, and it is kept.</b> <c>Maran.Modules.Identity</c> was its one entry: its
    /// <c>User.AccountId</c> is nullable and names the hosting account a CUSTOMER login owns, and in
    /// v1 the only user the panel constructs is the setup administrator, whose <c>AccountId</c> is
    /// null — so no row was keyed to an account and nothing was orphaned.
    /// </para>
    /// <para>
    /// That exemption was still wrong to keep, for two reasons neither of which was about orphans.
    /// The runtime residue auditor has no exemption list, so the first customer login ever created
    /// would have made every deletion of its account FAIL — the audit would find the row, refuse,
    /// and leave the operator with an account that could not be deleted and no path forward. And the
    /// obvious repair on the day would have been to widen this list, turning a loud refusal into a
    /// silent one: a login outliving its account is a working password against a tenant that is
    /// gone. Identity now has a subscriber, so neither is reachable.
    /// </para>
    /// <para>
    /// The dictionary stays because the exemption it holds is a real decision some module may
    /// legitimately need, and because the staleness guard below is what keeps such a decision from
    /// rotting into a place a real tenant table hides.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> Exempt =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The modules that run something on a customer's behalf which a SUSPENSION does not stop, each
    /// with the reason, so that what suspension does not cover is a fact of the build.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is documentation the compiler keeps honest, not an excuse list.</b> Suspending an
    /// account locks its own Linux login, replaces every vhost it owns with the suspended one,
    /// suppresses every managed entry of its crontab and locks every one of its SFTP logins. What it
    /// still does not reach is written down HERE, where
    /// <see cref="Every_module_named_as_uncovered_is_still_uncovered"/> makes it stop being writable
    /// the day it stops being true.
    /// </para>
    /// <para>
    /// An entry is removed in the same change that adds the module's handlers, and that removal is
    /// not optional: the test below FAILS on a module named here that has grown one. It has already
    /// fired once, for exactly that reason — Cron and Sftp were named here while they had no
    /// handlers, and the change that gave them handlers had to delete their entries and, with them,
    /// every sentence in the contract, the Sdk events and the two Accounts handlers that said an
    /// account's jobs kept firing and its logins kept working.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> NotCoveredBySuspension =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Maran.Modules.Databases"] =
                "an account's databases go on accepting connections while it is suspended, so a "
                + "customer whose panel access and file access are both gone can still read and "
                + "write their data from an application they host somewhere else. Revoking the "
                + "grants is the only thing that would stop it and restoring them EXACTLY is a real "
                + "reversal risk — a resume that regranted more than it revoked would widen access "
                + "silently. It is an open product decision and not an oversight, which is why it is "
                + "named here rather than left to be discovered.",
        };

    /// <summary>A module that pauses for a suspension also restores for a resumption.</summary>
    /// <remarks>
    /// <para>
    /// The asymmetry this forbids is the expensive one. A module that stops a resource and has no
    /// way to start it again turns every suspension into a one-way door: the account is reactivated,
    /// the panel says active, and whatever that module paused stays paused with nothing left to
    /// notice it. The reverse — restoring what was never paused — is the same defect read backwards.
    /// </para>
    /// <para>
    /// It is a symmetry law and not a coverage law on purpose. Which modules OUGHT to pause is a
    /// judgement no model walk can make, and the answer today is recorded in
    /// <see cref="NotCoveredBySuspension"/>; that a module which made the judgement implemented both
    /// halves is mechanical, and is what this asserts.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_module_that_pauses_for_a_suspension_also_restores_for_a_resumption()
    {
        var lopsided = ModuleAssemblies()
            .Where(assembly =>
            {
                return Handles(assembly, typeof(AccountSuspending)) != Handles(assembly, typeof(AccountResuming));
            })
            .Select(Name)
            .OrderBy(name => { return name; }, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            lopsided.Count == 0,
            $"These modules handle one of {nameof(AccountSuspending)}/{nameof(AccountResuming)} and "
            + "not the other, so a suspension they act on is a one-way door — reactivating the "
            + $"account leaves what they paused paused, or restores what was never stopped: {string.Join(", ", lopsided)}");
    }

    /// <summary>Every module named as uncovered by suspension is still uncovered by it.</summary>
    /// <remarks>
    /// <para>
    /// The staleness guard, and here it does more work than its deletion counterpart. A stale
    /// exemption for deletion is dead text; a stale entry here is a false statement about what a
    /// suspension does, sitting in the one place this repository has chosen to record that — and the
    /// handlers' own doc comments and the panel's task lines repeat it to an operator.
    /// </para>
    /// <para>
    /// So the day Cron grows a suspension handler, this test goes red and the sentence saying cron
    /// keeps firing has to be deleted in the same change. That is the property that was missing when
    /// the contract, the handler remark and the design note all went on describing a cascade nobody
    /// had written.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_module_named_as_uncovered_is_still_uncovered()
    {
        var byName = ModuleAssemblies().ToDictionary(Name, assembly => { return assembly; }, StringComparer.Ordinal);

        var missing = NotCoveredBySuspension.Keys
            .Where(name => { return !byName.ContainsKey(name); })
            .OrderBy(name => { return name; }, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These modules are named as not covered by suspension and are not composed modules at "
            + $"all, so the sentence describes nothing: {string.Join(", ", missing)}");

        var covered = NotCoveredBySuspension.Keys
            .Where(name =>
            {
                return Handles(byName[name], typeof(AccountSuspending))
                    || Handles(byName[name], typeof(AccountResuming));
            })
            .OrderBy(name => { return name; }, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            covered.Count == 0,
            "These modules now handle a suspension event while still being documented as not covered "
            + "by one. Remove the entry from AccountCascadeTests.NotCoveredBySuspension, and with it "
            + "every sentence that says the account keeps running this — the handler remarks on "
            + "SuspendAccountCommandHandler and ReactivateAccountCommandHandler, the AccountSuspending "
            + $"and AccountResuming doc comments, and accounts.proto: {string.Join(", ", covered)}");
    }

    /// <summary>The suspension probe finds the module that pauses and none of the ones that do not.</summary>
    /// <remarks>
    /// <para>
    /// The controls for both suspension tests, in both directions. The symmetry law is satisfied by
    /// a probe that answers "no" for everything, and the staleness guard is satisfied by a probe that
    /// answers "no" for everything too — so a positive control is the only thing that separates
    /// either from a census over nothing.
    /// </para>
    /// <para>
    /// Sites is the positive control because it is the one module piece 1 covers, and it must be
    /// found for BOTH events. <c>Maran.Sdk</c> is the negative control for the reason the deletion
    /// test gives: it DECLARES both records and may contain no handler at all
    /// (<c>ModuleIsolationTests</c>), so unlike a module it cannot expire as a control by acquiring
    /// one.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_suspension_probe_finds_the_module_that_pauses_and_none_of_the_ones_that_do_not()
    {
        var assemblies = ModuleAssemblies();

        Assert.True(assemblies.Count >= 4, $"Only {assemblies.Count} composed modules were found");

        var sites = Assert.Single(assemblies, assembly =>
        {
            return Name(assembly) == "Maran.Modules.Sites";
        });
        Assert.True(Handles(sites, typeof(AccountSuspending)), "the probe missed the Sites module's suspension handler");
        Assert.True(Handles(sites, typeof(AccountResuming)), "the probe missed the Sites module's resumption handler");

        Assert.False(
            Handles(typeof(AccountSuspending).Assembly, typeof(AccountSuspending)),
            "the probe claims a handler in the contract assembly, which declares the message and handles nothing");
        Assert.False(
            Handles(typeof(AccountResuming).Assembly, typeof(AccountResuming)),
            "the probe claims a handler in the contract assembly, which declares the message and handles nothing");

        // And the list of what suspension does not cover is not empty, which is the only thing that
        // makes the staleness guard above an assertion rather than a loop over nothing.
        Assert.NotEmpty(NotCoveredBySuspension);
    }

    /// <summary>Every module owning tenant rows subscribes to the account-deletion cascade.</summary>
    [Fact]
    public void Every_module_owning_tenant_rows_subscribes_to_the_cascade()
    {
        var silent = TenantAssemblies()
            .Where(assembly => { return !SubscribesToTheCascade(assembly); })
            .Select(Name)
            .Where(name => { return !Exempt.ContainsKey(name); })
            .OrderBy(name => { return name; }, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            silent.Count == 0,
            $"These modules own rows carrying an {TenantProperty} and handle no "
            + $"{nameof(AccountDeleting)}, so deleting an account leaves their rows behind and the "
            + $"panel goes on showing them: {string.Join(", ", silent)}. Add an "
            + "AccountDeletingHandler to the module, or add the module to AccountCascadeTests.Exempt "
            + "with the reason its rows may outlive the account.");
    }

    /// <summary>Every exemption still names a module that owns tenant rows.</summary>
    /// <remarks>
    /// The staleness guard. An exemption for a module that has stopped carrying an
    /// <see cref="TenantProperty"/> is dead text that reads like a decision, and the next module to
    /// take that name would inherit a judgement nobody made about it.
    /// </remarks>
    [Fact]
    public void Every_exemption_still_names_a_module_that_owns_tenant_rows()
    {
        var present = TenantAssemblies().Select(Name).ToHashSet(StringComparer.Ordinal);

        var stale = Exempt.Keys
            .Where(name => { return !present.Contains(name); })
            .OrderBy(name => { return name; }, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            stale.Count == 0,
            $"Exemptions for modules that no longer own rows carrying an {TenantProperty}: "
            + string.Join(", ", stale));
    }

    /// <summary>The probe finds a handler that is there and reports none where there is none.</summary>
    /// <remarks>
    /// <para>
    /// The controls, both directions, because this assertion is satisfied by an empty answer twice
    /// over. A census that found no tenant entity, and a probe that answered "subscribes" for
    /// everything, would each turn the test above green while looking at nothing.
    /// </para>
    /// <para>
    /// So: the census must find several modules; the probe must FIND the Databases module's handler,
    /// which is planted in the tree and has been there since the cascade was written; and it must
    /// answer NO for an assembly that genuinely has none.
    /// </para>
    /// <para>
    /// <b>The negative control is deliberately not a module.</b> It used to be Identity, the last
    /// module without a subscriber, and that made the control expire the moment the gap it was
    /// standing in for was closed — the control and the defect died together, which is the one thing
    /// a control must not do. It is now <c>Maran.Sdk</c>, the assembly that DECLARES
    /// <see cref="AccountDeleting"/> and handles nothing: a contract surface may not contain a
    /// handler at all (<c>ModuleIsolationTests</c>), so this control does not expire when the
    /// cascade becomes complete, which is the state the panel is supposed to be in from now on.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_cascade_probe_can_tell_a_subscriber_from_an_assembly_that_has_none()
    {
        var assemblies = TenantAssemblies();

        Assert.True(assemblies.Count >= 4, $"Only {assemblies.Count} modules were found to own tenant rows");

        var databases = Assert.Single(assemblies, assembly =>
        {
            return Name(assembly) == "Maran.Modules.Databases";
        });
        Assert.True(SubscribesToTheCascade(databases), "the probe missed the Databases module's handler");

        Assert.False(
            SubscribesToTheCascade(typeof(AccountDeleting).Assembly),
            "the probe claims a handler in the contract assembly, which declares the message and handles nothing");
    }

    /// <summary>
    /// Nothing a module owns can survive the row the cascade removes: every dependent of a tenant
    /// entity is mapped as a relationship, and every such relationship is cascaded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The second hop, which is where "nobody was listening" reappears one level down.</b> The
    /// runtime residue auditor counts rows carrying an <c>AccountId</c>. That is the whole of what
    /// it can see, so a module that deletes its tenant rows and leaves their DEPENDENTS behind is
    /// pronounced clean by the audit and reports a completed deletion — the same substitution that
    /// produced the original defect, one foreign key further away and invisible to the check written
    /// for it.
    /// </para>
    /// <para>
    /// It is not hypothetical: Identity's <c>Session</c>, <c>RecoveryCode</c> and
    /// <c>PasswordResetToken</c> are keyed by <c>UserId</c>, not by <c>AccountId</c>, and a reset
    /// token is a live permission to set a password rather than an untidy row.
    /// </para>
    /// <para>
    /// <b>Why it asks TWO questions, and what the first one cost.</b> An earlier version of this
    /// test asked only the second — that a relationship pointing at a tenant entity cascades — and a
    /// mutation removing <c>PasswordResetToken</c>'s foreign key outright SURVIVED it. Of course it
    /// did: deleting the relationship deletes the thing the test inspects, so the check went quiet
    /// on exactly the defect it was written for. <c>PasswordResetToken.UserId</c> really had been a
    /// user id by naming convention alone, with nothing in the database enforcing it. So the first
    /// question is about the ABSENCE of a relationship: a property named after another entity in the
    /// same model must actually point at it.
    /// </para>
    /// <para>
    /// <b>Same model, deliberately.</b> A module may hold another MODULE's identifier as a plain
    /// column and must not have a foreign key to it — <c>Certificate.SiteId</c> names a row in the
    /// Sites schema, which this module may not reference at all (rules/architecture.md). Those live
    /// in different contexts and different models, so the convention is asked only where a
    /// relationship is legal in the first place.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_dependent_of_a_tenant_row_is_removed_with_it()
    {
        var contexts = ModuleDbContexts.CreateAll();
        try
        {
            var unmapped = UnmappedReferences(contexts).ToList();

            Assert.True(
                unmapped.Count == 0,
                "These columns name another entity in the same module and are not mapped as a "
                + "relationship to it, so the database will not remove them with the row they name "
                + "and the residue audit — which only counts rows carrying an "
                + $"{TenantProperty} — cannot see them either: {string.Join(", ", unmapped)}");

            var relationships = TenantDependents(contexts).ToList();

            // The vacuity guard on the axis that can go blind: both assertions here are satisfied by
            // an empty census, and an empty census is exactly what a broken model walk produces.
            Assert.True(
                relationships.Count >= 3,
                $"Only {relationships.Count} dependents of a tenant entity were found; the model walk "
                + "has stopped seeing relationships and this test now proves nothing");

            var orphaning = relationships
                .Where(relationship =>
                {
                    return relationship.DeleteBehavior is not (DeleteBehavior.Cascade or DeleteBehavior.ClientCascade);
                })
                .Select(relationship =>
                {
                    return $"{relationship.DeclaringEntityType.ClrType.Name} -> "
                        + $"{relationship.PrincipalEntityType.ClrType.Name} ({relationship.DeleteBehavior})";
                })
                .OrderBy(text => { return text; }, StringComparer.Ordinal)
                .ToList();

            Assert.True(
                orphaning.Count == 0,
                "These rows hang off an entity keyed by an account and are not removed with it, so "
                + "the account-deletion cascade leaves them behind where the residue audit — which "
                + $"only counts rows carrying an {TenantProperty} — cannot see them: "
                + string.Join(", ", orphaning));
        }
        finally
        {
            foreach (var context in contexts)
            {
                context.Dispose();
            }
        }
    }

    /// <summary>The probe for an unmapped reference reports one where there is none.</summary>
    /// <remarks>
    /// The inverse control for the first assertion above, which is a refusing gate and so owes proof
    /// that it also ACCEPTS. <c>Session.UserId</c> is exactly the shape the gate is written to catch
    /// and is correctly mapped, so a gate that refused everything would report it — and it must not.
    /// The second half is the vacuity guard on the population: a gate that considered no property at
    /// all would report nothing too, which reads identically from the outside.
    /// </remarks>
    [Fact]
    public void The_unmapped_reference_probe_accepts_a_column_that_is_mapped()
    {
        var contexts = ModuleDbContexts.CreateAll();
        try
        {
            var reported = UnmappedReferences(contexts).ToList();

            Assert.DoesNotContain("Session.UserId", reported);

            // And the probe is looking at something: the census of properties it CONSIDERS must be
            // non-empty, or "nothing was reported" would be the answer a probe that examined no
            // property at all would also give.
            Assert.True(
                CandidateReferences(contexts).Count() >= 3,
                "the probe considered fewer than three entity-named columns, so it is not looking");
        }
        finally
        {
            foreach (var context in contexts)
            {
                context.Dispose();
            }
        }
    }

    /// <summary>Every mapped relationship whose principal is an entity keyed by an account.</summary>
    /// <param name="contexts">The module contexts whose models are read.</param>
    /// <returns>The foreign keys pointing at a tenant entity.</returns>
    private static IEnumerable<IForeignKey> TenantDependents(IReadOnlyCollection<DbContext> contexts)
    {
        return contexts
            .SelectMany(context => { return context.Model.GetEntityTypes(); })
            .SelectMany(entity => { return entity.GetForeignKeys(); })
            .Where(foreignKey =>
            {
                return foreignKey.PrincipalEntityType.FindProperty(TenantProperty) is not null;
            });
    }

    /// <summary>
    /// Every column named after another entity in the same model, which is the population the
    /// unmapped-reference gate judges.
    /// </summary>
    /// <param name="contexts">The module contexts whose models are read.</param>
    /// <returns>The candidate property and the entity it names.</returns>
    private static IEnumerable<(IEntityType Entity, IProperty Property, IEntityType Names)> CandidateReferences(
        IReadOnlyCollection<DbContext> contexts)
    {
        foreach (var context in contexts)
        {
            var byName = context.Model.GetEntityTypes()
                .ToDictionary(entity => { return entity.ClrType.Name; }, StringComparer.Ordinal);

            foreach (var entity in context.Model.GetEntityTypes())
            {
                foreach (var property in entity.GetProperties())
                {
                    if (!property.Name.EndsWith("Id", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var named = property.Name[..^"Id".Length];
                    if (named.Length == 0
                        || named == entity.ClrType.Name
                        || !byName.TryGetValue(named, out var principal))
                    {
                        continue;
                    }

                    yield return (entity, property, principal);
                }
            }
        }
    }

    /// <summary>The candidate columns that name an entity without being mapped as pointing at it.</summary>
    /// <param name="contexts">The module contexts whose models are read.</param>
    /// <returns>The offending columns, as <c>Entity.Property</c>, in a stable order.</returns>
    private static IEnumerable<string> UnmappedReferences(IReadOnlyCollection<DbContext> contexts)
    {
        return CandidateReferences(contexts)
            .Where(candidate =>
            {
                return !candidate.Entity.GetForeignKeys().Any(foreignKey =>
                {
                    return foreignKey.PrincipalEntityType == candidate.Names
                        && foreignKey.Properties.Contains(candidate.Property);
                });
            })
            .Select(candidate => { return $"{candidate.Entity.ClrType.Name}.{candidate.Property.Name}"; })
            .OrderBy(text => { return text; }, StringComparer.Ordinal);
    }

    /// <summary>Every module assembly whose mapping declares a row keyed by an account.</summary>
    /// <returns>One assembly per module owning tenant rows, deduplicated.</returns>
    private static List<Assembly> TenantAssemblies()
    {
        var contexts = ModuleDbContexts.CreateAll();
        try
        {
            return contexts
                .SelectMany(context => { return context.Model.GetEntityTypes(); })
                .Where(entity => { return entity.FindProperty(TenantProperty) is not null; })
                .Select(entity => { return entity.ClrType.Assembly; })
                .Distinct()
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

    /// <summary>Whether any type in the assembly handles <see cref="AccountDeleting"/>.</summary>
    /// <param name="assembly">The module assembly to search.</param>
    /// <returns><c>true</c> when a public instance method takes the event as its first argument.</returns>
    private static bool SubscribesToTheCascade(Assembly assembly)
    {
        return Handles(assembly, typeof(AccountDeleting));
    }

    /// <summary>Whether any type in the assembly handles <paramref name="message"/>.</summary>
    /// <param name="assembly">The module assembly to search.</param>
    /// <param name="message">The event type a handler would take as its first argument.</param>
    /// <returns><c>true</c> when a public instance method takes that event as its first argument.</returns>
    /// <remarks>
    /// The shape is asked of the METHOD rather than of the type's name, because the name is a
    /// convention and the signature is what the message bus resolves. A handler renamed, or a second
    /// one added beside it, still counts; a class called <c>AccountDeletingHandler</c> that handles
    /// nothing does not.
    ///
    /// The message's OWN members are excluded, and finding that out is what the inverse controls are
    /// for. Every one of these events is a record, so the compiler writes it an <c>Equals(T)</c> —
    /// a public instance method taking the event as its first argument — which made the contract
    /// assembly that merely DECLARES the message read as a subscriber to it. Any real handler is
    /// declared somewhere else.
    /// </remarks>
    private static bool Handles(Assembly assembly, Type message)
    {
        return assembly.GetTypes()
            .Where(type => { return type is { IsAbstract: false, IsPublic: true }; })
            .SelectMany(type => { return type.GetMethods(BindingFlags.Public | BindingFlags.Instance); })
            .Any(method =>
            {
                return method.DeclaringType != message
                    && method.GetParameters().FirstOrDefault()?.ParameterType == message;
            });
    }

    /// <summary>Every composed module's assembly, as the panel itself lists them.</summary>
    /// <returns>One assembly per module in <c>ModuleRegistry</c>, deduplicated.</returns>
    /// <remarks>
    /// The suspension laws below ask the REGISTRY and not the model, and that difference is the
    /// whole reason they can see Cron at all. The deletion law above asks the model because its
    /// question is "who owns a customer's rows"; the suspension question is "who runs something on a
    /// customer's behalf", and the module most at risk of running something through a suspension —
    /// Cron — owns no row in any schema, because the account's crontab on the host IS its state. A
    /// model walk is blind to exactly that module, so a census built on one would be satisfied by
    /// silence where silence is the defect.
    /// </remarks>
    private static List<Assembly> ModuleAssemblies()
    {
        return ModuleRegistry.All
            .Select(module => { return module.GetType().Assembly; })
            .Distinct()
            .ToList();
    }

    /// <summary>The module name an assembly is reported and exempted under.</summary>
    /// <param name="assembly">The module assembly.</param>
    /// <returns>Its simple name.</returns>
    private static string Name(Assembly assembly)
    {
        return assembly.GetName().Name ?? assembly.FullName ?? string.Empty;
    }
}
