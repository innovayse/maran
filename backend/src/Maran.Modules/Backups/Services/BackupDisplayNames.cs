using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Backups.Services;

/// <summary>Resolves the operator-facing names of a backup's status, kind and verification state.</summary>
/// <remarks>
/// <b>The names are the backend's to produce, not the SPA's</b> (rules/architecture.md "The backend
/// owns the data, the SPA renders it"). Both sets are closed and every member has an entry, so a miss
/// is a defect and not a fallback case. The options endpoint and the backup views draw their labels
/// from the same resource keys, so the word a filter offers and the word a badge prints cannot
/// differ.
/// </remarks>
public sealed class BackupDisplayNames
{
    /// <summary>The prefix a status's resx key carries.</summary>
    private const string StatusPrefix = "BackupStatus";

    /// <summary>The prefix a kind's resx key carries.</summary>
    private const string KindPrefix = "BackupKind";

    /// <summary>The prefix a verification state's resx key carries.</summary>
    private const string VerificationStatePrefix = "BackupVerificationState";

    /// <summary>Resolves this module's display-name resources for the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the resolver.</summary>
    /// <param name="displayNames">This module's display-name resources.</param>
    public BackupDisplayNames(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Names how far a backup's run got, as an operator reads it.</summary>
    /// <param name="status">The status to name.</param>
    /// <returns>The localized name.</returns>
    public string Of(BackupStatus status)
    {
        return _displayNames[StatusPrefix + status].Value;
    }

    /// <summary>Names why a backup was taken, as an operator reads it.</summary>
    /// <param name="kind">The kind to name.</param>
    /// <returns>The localized name.</returns>
    public string Of(BackupKind kind)
    {
        return _displayNames[KindPrefix + kind].Value;
    }

    /// <summary>Names whether a backup's artifact was read back and found sound, as an operator reads it.</summary>
    /// <param name="state">The verification state to name.</param>
    /// <returns>The localized name.</returns>
    public string Of(BackupVerificationState state)
    {
        return _displayNames[VerificationStatePrefix + state].Value;
    }
}
