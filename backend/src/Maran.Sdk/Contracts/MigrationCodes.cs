using Maran.Sdk.Resources;

namespace Maran.Sdk.Contracts;

/// <summary>
/// The machine-stable codes that more than one module raises while a package is judged and imported (issue
/// #127), spelled once.
/// </summary>
/// <remarks>
/// <para>
/// The text of each lives in this assembly's <c>ErrorMessages</c> triple, which the build turns into an
/// internal class; a module cannot name a member of it, so the Sdk republishes the names it shares. Each
/// constant is <c>nameof</c> a resx key, so a code that is not a key is a build error exactly as it is for a
/// module's own codes, and a module that raises one never writes the string.
/// </para>
/// <para>
/// A finding and a failure use the same code: the dry run states a conflict with it, and the import that was
/// started anyway fails with it. The kind of each (the HTTP status it answers) is decided in
/// <c>ExpectedErrorStatuses</c>.
/// </para>
/// </remarks>
public static class MigrationCodes
{
    /// <summary>A module's section of the package cannot be read: unknown version, another module's, or not the shape it wrote.</summary>
    public const string SectionInvalid = nameof(ErrorMessages.MigrationSectionInvalid);

    /// <summary>An importer was asked to import before the account it imports into exists.</summary>
    public const string TargetAccountMissing = nameof(ErrorMessages.MigrationTargetAccountMissing);

    /// <summary>A name the package keeps is already used on this server; names are never renamed (decision D1).</summary>
    public const string NameTaken = nameof(ErrorMessages.MigrationNameTaken);

    /// <summary>A domain the package carries is already hosted on this server.</summary>
    public const string DomainTaken = nameof(ErrorMessages.MigrationDomainTaken);

    /// <summary>The package holds more of a kind of resource than the chosen plan allows.</summary>
    public const string PlanLimitExceeded = nameof(ErrorMessages.MigrationPlanLimitExceeded);

    /// <summary>The plan the administrator chose does not exist on this server.</summary>
    public const string PlanMissing = nameof(ErrorMessages.MigrationPlanMissing);

    /// <summary>A site needs a PHP version that is not installed on this server.</summary>
    public const string PhpVersionMissing = nameof(ErrorMessages.MigrationPhpVersionMissing);

    /// <summary>Cron entries are imported switched off, and the administrator enables them after the move.</summary>
    public const string CronImportedDisabled = nameof(ErrorMessages.MigrationCronImportedDisabled);

    /// <summary>An ACME certificate is not carried over; the administrator issues it again once the name points at this server (decision D2).</summary>
    public const string CertificateReissue = nameof(ErrorMessages.MigrationCertificateReissue);

    /// <summary>A custom certificate is not carried over, because its key never leaves its server; the administrator uploads it again (decision D2).</summary>
    public const string CertificateUpload = nameof(ErrorMessages.MigrationCertificateUpload);

    /// <summary>A backup with the artifact's identifier already exists on this server.</summary>
    public const string BackupIdTaken = nameof(ErrorMessages.MigrationBackupIdTaken);

    /// <summary>A rollback was asked to delete an account the import did not create, and refused.</summary>
    public const string RollbackRefused = nameof(ErrorMessages.MigrationRollbackRefused);
}
