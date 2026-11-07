namespace Maran.Sdk.Contracts;

/// <summary>The one person or account a personal-data export is about.</summary>
/// <param name="Kind">Whether <paramref name="Id"/> names a user or a hosting account.</param>
/// <param name="Id">The user id or the account id, as <paramref name="Kind"/> says.</param>
public sealed record PersonalDataSubject(PersonalDataSubjectKind Kind, Guid Id);
