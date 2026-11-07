using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// A planted provider that answers nothing, registered by the personal-data census's accepting control so
/// the registration probe has a registration to find.
/// </summary>
/// <remarks>
/// Never composed by the panel and never reached through <c>ModuleRegistry</c>; only the census test that
/// needs a registration it can be certain about constructs a service collection holding it.
/// </remarks>
public sealed class PlantedPersonalDataProvider : IPersonalDataProvider
{
    /// <inheritdoc />
    public Task<IReadOnlyList<PersonalDataSection>> ExportAsync(PersonalDataSubject subject, CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<PersonalDataSection>>([]);
    }
}
