namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// What the personal-data census knows about one composed module: whether it holds an account's rows and
/// whether its registrations include a personal-data provider.
/// </summary>
/// <param name="Module">The module's assembly name, which is how the exemption lists name it.</param>
/// <param name="RequiresProvider">Whether the module holds an account's rows and so owes the export a provider.</param>
/// <param name="RegistersProvider">Whether the module's registrations include an <c>IPersonalDataProvider</c>.</param>
public sealed record ModuleProviderFact(string Module, bool RequiresProvider, bool RegistersProvider);
