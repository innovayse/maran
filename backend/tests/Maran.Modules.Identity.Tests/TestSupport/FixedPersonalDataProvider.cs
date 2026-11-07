using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// A personal-data provider that answers one fixed section for an account subject and nothing for a
/// user subject, and remembers every subject it was asked about, so a test can see that the handler
/// consulted it and with what.
/// </summary>
public sealed class FixedPersonalDataProvider : IPersonalDataProvider
{
    /// <summary>The section returned for an account subject.</summary>
    private readonly PersonalDataSection _section;

    /// <summary>The subjects this provider was asked about, in order.</summary>
    public List<PersonalDataSubject> Asked { get; } = [];

    /// <summary>Creates the provider.</summary>
    /// <param name="section">The section to return for an account subject.</param>
    public FixedPersonalDataProvider(PersonalDataSection section)
    {
        _section = section;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PersonalDataSection>> ExportAsync(PersonalDataSubject subject, CancellationToken ct)
    {
        Asked.Add(subject);

        IReadOnlyList<PersonalDataSection> answer = subject.Kind == PersonalDataSubjectKind.Account
            ? [_section]
            : [];

        return Task.FromResult(answer);
    }
}
