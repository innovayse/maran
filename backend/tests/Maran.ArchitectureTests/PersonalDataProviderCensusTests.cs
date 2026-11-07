using Maran.ArchitectureTests.Fixtures;
using Maran.Host.Modules;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.ArchitectureTests;

/// <summary>
/// Makes the personal-data export complete by construction in the one respect a machine can see: a module
/// that holds an account's rows registers an <see cref="IPersonalDataProvider"/>, so a module added
/// tomorrow cannot leave a customer's data out of the export without a test going red.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this proves, and what it cannot.</b> It proves a provider EXISTS for every module that owns
/// account rows. It does not prove a provider exports every field of its entity (a new personal column
/// missing from the export is invisible here) and it does not prove a field it exports is harmless: the
/// per-provider unit tests plant a secret and look for it. The census is the half of the guarantee that
/// does not depend on anyone remembering a module.
/// </para>
/// <para>
/// <b>Who owes a provider.</b> A module whose model carries an entity with an <c>AccountId</c>, read from
/// the same EF models <see cref="TenantScopeTests"/> reads, plus the modules named in
/// <see cref="AlsoRequired"/>: Accounts holds the account itself, whose key is <c>Id</c> and not
/// <c>AccountId</c>, so the model walk cannot see it and the requirement is stated by name.
/// </para>
/// <para>
/// <b>Exemptions are named, reasoned and checked for staleness</b>, in the form of
/// <see cref="TenantScopeTests"/>. The list is empty today: every module that holds an account's rows
/// exports them. It exists so the day a module genuinely must not (rows that are a server's, not a
/// person's) the decision is written down beside its reason instead of being a missing registration
/// nobody can tell from an oversight.
/// </para>
/// <para>
/// <b>How a registration is observed.</b> The module's own <c>ConfigureServices</c> is run against a fresh
/// service collection and an empty configuration, and the descriptors are read: the registration the Host
/// would make, not a type that happens to implement the interface. A module whose registration needs
/// configuration to be made at all would throw here, which is a loud failure and not a quiet pass.
/// </para>
/// </remarks>
public sealed class PersonalDataProviderCensusTests
{
    /// <summary>
    /// Modules that owe a provider although their model has no entity carrying an <c>AccountId</c>, each with
    /// the reason.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AlsoRequired =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Maran.Modules.Accounts"] =
                "holds the account row itself, keyed by Id: the owner's address and language are personal data, and the model walk looks for an AccountId column the account does not have",
        };

    /// <summary>
    /// Modules that hold an account's rows and are deliberately without a provider, each with the reason it is
    /// safe. Empty today; see the class remarks.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Exempt =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Every module that holds an account's rows registers a personal-data provider.</summary>
    [Fact]
    public void Every_module_that_holds_an_accounts_rows_registers_a_personal_data_provider()
    {
        var facts = ComposedModuleFacts();

        var missing = Offenders(facts);

        Assert.True(
            missing.Count == 0,
            "These modules hold an account's rows and register no IPersonalDataProvider, so the personal-data "
            + $"export leaves that data out: {string.Join(", ", missing)}. Add a <Module>PersonalDataProvider "
            + "in the module's Services/ and register it as AddScoped<IPersonalDataProvider, ...> in the "
            + "module, or add the module to PersonalDataProviderCensusTests.Exempt with the reason it is safe.");
    }

    /// <summary>The census finds every module that declares an entity with an account id.</summary>
    /// <remarks>
    /// The second source, as <see cref="TenantScopeTests"/> has: reflection over the module assemblies'
    /// entity types is a different source from the EF models the requirement is read from, so a module whose
    /// entity stops being MAPPED is named here instead of quietly leaving the set of modules that owe a
    /// provider.
    /// </remarks>
    [Fact]
    public void Census_finds_every_module_that_declares_an_entity_with_an_account_id()
    {
        var expected = TenantScopeCensus.EntityTypesDeclaringTenantProperty()
            .Select(type => { return type.Assembly.GetName().Name!; })
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(
            expected.Count > 0,
            "No entity type declaring an AccountId was found in any Maran.Modules.* assembly beside the test "
            + "binary, so this control derived its expectation from nothing and would agree with a census that "
            + "found nothing.");

        var required = ComposedModuleFacts()
            .Where(fact => { return fact.RequiresProvider && !AlsoRequired.ContainsKey(fact.Module); })
            .Select(fact => { return fact.Module; })
            .ToHashSet(StringComparer.Ordinal);

        var unseen = expected
            .Where(module => { return !required.Contains(module); })
            .OrderBy(module => { return module; }, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unseen.Count == 0,
            $"These modules declare an entity with an AccountId and the census did not require a provider of "
            + $"them: {string.Join(", ", unseen)}. Either the module is missing from ModuleRegistry.All or its "
            + "entity is not mapped by its DbContext.");
    }

    /// <summary>The census names a planted module that holds account rows and registers nothing.</summary>
    /// <remarks>
    /// The refusing control, over the facts and over the registration probe. Without it every test above is
    /// satisfied by a census that has stopped finding anything.
    /// </remarks>
    [Fact]
    public void The_census_names_a_planted_module_with_account_rows_and_no_provider()
    {
        var planted = new ModuleProviderFact(
            "Maran.Modules.Planted", RequiresProvider: true, RegistersProvider: false);

        var offenders = Offenders([planted]);

        Assert.Equal(["Maran.Modules.Planted"], offenders);
        Assert.False(RegistersProvider(new ServiceCollection()));
    }

    /// <summary>The census accepts a planted module with a provider, and one that holds no account rows.</summary>
    /// <remarks>
    /// The accepting control: a census mutated to refuse everything passes every test that only ever hands
    /// it a broken module. The probe is shown to FIND a registration, so the refusal above is not a probe that
    /// reads nothing.
    /// </remarks>
    [Fact]
    public void The_census_accepts_a_planted_module_with_a_provider_and_one_without_account_rows()
    {
        var services = new ServiceCollection();
        services.AddScoped<IPersonalDataProvider, PlantedPersonalDataProvider>();

        var withProvider = new ModuleProviderFact(
            "Maran.Modules.Planted", RequiresProvider: true, RegistersProvider: RegistersProvider(services));
        var withoutRows = new ModuleProviderFact(
            "Maran.Modules.Other", RequiresProvider: false, RegistersProvider: false);

        Assert.True(RegistersProvider(services));
        Assert.Empty(Offenders([withProvider, withoutRows]));
    }

    /// <summary>Every named requirement and exemption still names a composed module it applies to.</summary>
    /// <remarks>
    /// The staleness guard. A requirement or an exemption for a renamed or removed module is dead text that
    /// reads like a decision, and the next module to take the name would inherit it. An exemption for a
    /// module that now registers a provider (or no longer owes one) is stale too: it would hide the day the
    /// provider is deleted.
    /// </remarks>
    [Fact]
    public void Every_named_requirement_and_exemption_still_names_a_module_it_applies_to()
    {
        var facts = ComposedModuleFacts().ToDictionary(fact => { return fact.Module; }, StringComparer.Ordinal);

        var gone = AlsoRequired.Keys.Concat(Exempt.Keys)
            .Where(module => { return !facts.ContainsKey(module); })
            .OrderBy(module => { return module; }, StringComparer.Ordinal)
            .ToList();

        var stale = Exempt.Keys
            .Where(module =>
            {
                return facts.TryGetValue(module, out var fact) && (!fact.RequiresProvider || fact.RegistersProvider);
            })
            .OrderBy(module => { return module; }, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            gone.Count == 0,
            $"Named modules the panel no longer composes — renamed or removed: {string.Join(", ", gone)}");
        Assert.True(
            stale.Count == 0,
            "Exemptions for modules that now register a provider or no longer hold an account's rows: "
            + string.Join(", ", stale));
    }

    /// <summary>The names of the modules that owe a provider, are not exempt and register none.</summary>
    /// <param name="facts">What is known about each module.</param>
    /// <returns>The offending module names, ordered.</returns>
    private static List<string> Offenders(IEnumerable<ModuleProviderFact> facts)
    {
        return facts
            .Where(fact =>
            {
                return fact.RequiresProvider && !fact.RegistersProvider && !Exempt.ContainsKey(fact.Module);
            })
            .Select(fact => { return fact.Module; })
            .OrderBy(module => { return module; }, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Reads, for every composed module, whether it owes a provider and whether it registers one.</summary>
    /// <returns>One fact per module in <c>ModuleRegistry.All</c>.</returns>
    private static List<ModuleProviderFact> ComposedModuleFacts()
    {
        var contexts = ModuleDbContexts.CreateAll();
        try
        {
            var holders = TenantScopeCensus.CarryingTenantProperty(contexts)
                .Select(entity => { return entity.ClrType.Assembly.GetName().Name!; })
                .ToHashSet(StringComparer.Ordinal);

            return ModuleRegistry.All
                .Select(module =>
                {
                    var name = module.GetType().Assembly.GetName().Name!;
                    var requires = holders.Contains(name) || AlsoRequired.ContainsKey(name);

                    return new ModuleProviderFact(name, requires, RegistersProvider(module));
                })
                .OrderBy(fact => { return fact.Module; }, StringComparer.Ordinal)
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

    /// <summary>Runs a module's own registration and says whether it registers a personal-data provider.</summary>
    /// <param name="module">The module to register.</param>
    /// <returns>True when the registrations include an <see cref="IPersonalDataProvider"/>.</returns>
    private static bool RegistersProvider(IPanelModule module)
    {
        var services = new ServiceCollection();
        module.ConfigureServices(services, new ConfigurationBuilder().Build());

        return RegistersProvider(services);
    }

    /// <summary>Says whether a service collection registers an <see cref="IPersonalDataProvider"/>.</summary>
    /// <param name="services">The collection to read.</param>
    /// <returns>True when a descriptor has the interface as its service type.</returns>
    private static bool RegistersProvider(IServiceCollection services)
    {
        return services.Any(descriptor => { return descriptor.ServiceType == typeof(IPersonalDataProvider); });
    }
}
