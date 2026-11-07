namespace Maran.Sdk.Contracts;

/// <summary>Whose personal data an export or an erasure is about.</summary>
public enum PersonalDataSubjectKind
{
    /// <summary>One person's login, and what the panel holds about that person.</summary>
    User = 1,

    /// <summary>One hosting account, and what the panel holds against it.</summary>
    Account = 2,
}
