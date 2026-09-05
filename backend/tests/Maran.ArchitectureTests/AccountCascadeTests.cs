using System.Reflection;
using Maran.ArchitectureTests.Fixtures;
using Maran.Sdk.Events;

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
    /// <c>Maran.Modules.Identity</c> is the whole list. Its <c>User.AccountId</c> is nullable and
    /// names the hosting account a CUSTOMER login owns; in v1 the only user the panel ever
    /// constructs is the setup administrator, whose <c>AccountId</c> is null, so no row in this
    /// table is keyed to an account at all. The column is the shape of a login the product does not
    /// yet create.
    /// </para>
    /// <para>
    /// The day it does, this exemption is wrong and must be removed rather than re-justified: a
    /// customer login surviving its account is a working password against a tenant that no longer
    /// exists, which is worse than any leftover row. This comment is the whole handover, and the
    /// staleness guard below is what keeps it from rotting into a place a real tenant table hides.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> Exempt =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Maran.Modules.Identity"] =
                "User.AccountId is the shape of a customer login v1 never creates; only the "
                + "administrator exists, and its AccountId is null",
        };

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
    /// answer NO for a module that genuinely has none. Identity is that module, which is why its
    /// exemption is load-bearing here as well — the day Identity gains a handler, this control fails
    /// and whoever added it is told to move the inverse control rather than to delete it.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_cascade_probe_can_tell_a_subscriber_from_a_module_that_has_none()
    {
        var assemblies = TenantAssemblies();

        Assert.True(assemblies.Count >= 4, $"Only {assemblies.Count} modules were found to own tenant rows");

        var databases = Assert.Single(assemblies, assembly =>
        {
            return Name(assembly) == "Maran.Modules.Databases";
        });
        Assert.True(SubscribesToTheCascade(databases), "the probe missed the Databases module's handler");

        var identity = Assert.Single(assemblies, assembly =>
        {
            return Name(assembly) == "Maran.Modules.Identity";
        });
        Assert.False(SubscribesToTheCascade(identity), "the probe claims a handler the Identity module has not got");
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
    /// <remarks>
    /// The shape is asked of the METHOD rather than of the type's name, because the name is a
    /// convention and the signature is what the message bus resolves. A handler renamed, or a second
    /// one added beside it, still counts; a class called <c>AccountDeletingHandler</c> that handles
    /// nothing does not.
    /// </remarks>
    private static bool SubscribesToTheCascade(Assembly assembly)
    {
        return assembly.GetTypes()
            .Where(type => { return type is { IsAbstract: false, IsPublic: true }; })
            .SelectMany(type => { return type.GetMethods(BindingFlags.Public | BindingFlags.Instance); })
            .Any(method =>
            {
                return method.GetParameters().FirstOrDefault()?.ParameterType == typeof(AccountDeleting);
            });
    }

    /// <summary>The module name an assembly is reported and exempted under.</summary>
    /// <param name="assembly">The module assembly.</param>
    /// <returns>Its simple name.</returns>
    private static string Name(Assembly assembly)
    {
        return assembly.GetName().Name ?? assembly.FullName ?? string.Empty;
    }
}
