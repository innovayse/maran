namespace Maran.Modules.ServerMigration.Tests.TestSupport;

/// <summary>
/// The error codes this module raises, spelled once for the tests that assert them. The generated
/// <c>ErrorMessages</c> class is internal to the module, so a test cannot name it; the hazard of a spelled string
/// is drift from the resx, and <c>ErrorMessagesTests</c> closes it by checking every constant against the three files.
/// </summary>
public static class MigrationErrorCodes
{
    /// <summary>The server needs a confirmed host key first.</summary>
    public const string RemoteServerHostKeyUnconfirmed = "RemoteServerHostKeyUnconfirmed";

    /// <summary>The server is not in a state that allows a discovery.</summary>
    public const string RemoteServerNotReady = "RemoteServerNotReady";

    /// <summary>Every code the module raises, for the test that checks each against the resx triple.</summary>
    public static readonly string[] All =
    [
        "MigrationAccountCancelled",
        "MigrationAccountNameConfirmationMismatch",
        "MigrationAccountNotFound",
        "MigrationAccountStateInvalid",
        "MigrationAgentUnavailable",
        "MigrationBackupsUnavailable",
        "MigrationCronNotMigrated",
        "MigrationDatabaseNameNotImportable",
        "MigrationDiscoveryFailed",
        "MigrationDiskInsufficient",
        "MigrationDumpFailed",
        "MigrationDumpLoadFailed",
        "MigrationDumpRefused",
        "MigrationDumpTooLarge",
        "MigrationDuplicateInJob",
        "MigrationExportNotFound",
        "MigrationFingerprintMismatch",
        "MigrationGrantMissing",
        "MigrationGrantNotFound",
        "MigrationImportFaulted",
        "MigrationImportHasConflicts",
        "MigrationImportNotFound",
        "MigrationImportNotReady",
        "MigrationInitiatorGone",
        "MigrationJobAccountNotFound",
        "MigrationJobHasConflicts",
        "MigrationJobNoAccounts",
        "MigrationJobNotEditable",
        "MigrationJobNotFound",
        "MigrationJobStateInvalid",
        "MigrationLimitInvalid",
        "MigrationMailNotMigrated",
        "MigrationMappingIncomplete",
        "MigrationPackageMalformed",
        "MigrationPackageSchemaUnknown",
        "MigrationPackageTooLarge",
        "MigrationPullAddressRefused",
        "MigrationPullFailed",
        "MigrationPullIncomplete",
        "MigrationPullRefused",
        "MigrationPullUnreachable",
        "MigrationRollbackUnavailable",
        "MigrationSecretsAlreadyClaimed",
        "MigrationSectionNotImportable",
        "MigrationSignatureInvalid",
        "MigrationSiteProxyNotImported",
        "MigrationTransferFaulted",
        "RemoteServerAddressRefused",
        "RemoteServerAuthRefused",
        "RemoteServerEndpointTaken",
        "RemoteServerHostKeyChanged",
        "RemoteServerHostKeyFingerprintMismatch",
        RemoteServerHostKeyUnconfirmed,
        "RemoteServerInUse",
        "RemoteServerNameTaken",
        "RemoteServerNotFound",
        RemoteServerNotReady,
        "RemoteServerUnreachable",
    ];

    /// <summary>Every code a validator of the module raises, for the same check.</summary>
    public static readonly string[] Validation =
    [
        "FingerprintInvalid",
        "MigrationPullGrantInvalid",
        "MigrationPullUrlInvalid",
        "RemoteServerAllowedFromInvalid",
        "RemoteServerHostInvalid",
        "RemoteServerHostRequired",
        "RemoteServerNameMaxLength",
        "RemoteServerNameRequired",
        "RemoteServerPortInvalid",
        "RemoteServerSshUserInvalid",
    ];
}
