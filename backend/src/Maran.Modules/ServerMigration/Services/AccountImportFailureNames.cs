namespace Maran.Modules.ServerMigration.Services;

/// <summary>
/// Names the reason an import stopped in the caller's language, from the code the job stored (issue #127).
/// </summary>
/// <remarks>
/// A stored failure is resolved when it is READ, through the panel's one text provider, because the row outlives
/// the request that wrote it and a sentence stored then would be frozen in one language (rules/csharp.md "The
/// backend owns all user-facing message text"). The code is a code of any module (the first importer to fail names
/// its own), so a code no loaded table claims reads as this module's generic faulted sentence and never as the code.
/// </remarks>
public sealed class AccountImportFailureNames
{
    /// <summary>Resolves a code to the sentence for the current request culture.</summary>
    private readonly IErrorTextProvider _texts;

    /// <summary>Creates the resolver.</summary>
    /// <param name="texts">Resolves a code to the sentence for the current request culture.</param>
    public AccountImportFailureNames(IErrorTextProvider texts)
    {
        _texts = texts;
    }

    /// <summary>Names a stored failure.</summary>
    /// <param name="failureCode">The code the job stored, or <c>null</c> for an import that did not fail.</param>
    /// <returns>
    /// <c>null</c> when there is no failure; otherwise the sentence for the code, or the generic faulted sentence
    /// when no table claims the code.
    /// </returns>
    public string? Of(string? failureCode)
    {
        if (failureCode is null)
        {
            return null;
        }

        return _texts.TryResolve(failureCode, out var text)
            ? text
            : _texts.Resolve(nameof(Resources.ErrorMessages.MigrationImportFaulted));
    }
}
