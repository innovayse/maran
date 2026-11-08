//! The closed list of agent rpcs that yield an audit line.

/// Every agent rpc that changes the host or starts such work, as the full gRPC
/// path the request carries (`/maran.agent.v1.<Service>/<Method>`).
///
/// "Changes" is read from each rpc's doc comment in `proto/agent/v1/`: state,
/// files, accounts, configuration, services, the firewall, databases, logins or
/// sessions, or the start of work that does so. An rpc that only reports state
/// is in `READ_ONLY_RPCS` instead.
///
/// The list is closed and hand-written: a new rpc is classified in the change
/// that adds it, and the census test in `src/tests/audit/` fails when a path
/// of the contract is in neither list, in both, or no longer exists. The
/// census proves coverage, not correctness: a mutating rpc filed as read-only
/// is invisible to it, which is why the list is short enough to read whole.
///
/// Sorted in byte order and free of duplicates, as the census also checks.
pub const MUTATING_RPCS: &[&str] = &[
    "/maran.agent.v1.AccountsService/CreateAccount",
    "/maran.agent.v1.AccountsService/DeleteAccount",
    "/maran.agent.v1.AccountsService/RepairAccountHomeGroups",
    "/maran.agent.v1.AccountsService/SetAccountOpenFilesLimit",
    "/maran.agent.v1.AccountsService/SetAccountQuota",
    "/maran.agent.v1.AccountsService/SetAccountResourceLimits",
    "/maran.agent.v1.AccountsService/SuspendAccount",
    "/maran.agent.v1.AccountsService/UnsuspendAccount",
    "/maran.agent.v1.BackupService/CreateBackup",
    "/maran.agent.v1.BackupService/DeleteBackup",
    "/maran.agent.v1.BackupService/RestoreBackup",
    "/maran.agent.v1.CronService/CreateCronEntry",
    "/maran.agent.v1.CronService/DeleteCronEntry",
    "/maran.agent.v1.CronService/RunCronEntry",
    "/maran.agent.v1.CronService/SetAccountCronSuspended",
    "/maran.agent.v1.CronService/SetCronEntryEnabled",
    "/maran.agent.v1.CronService/SetCronEnvironment",
    "/maran.agent.v1.CronService/UpdateCronEntry",
    "/maran.agent.v1.DbService/CreateDatabase",
    "/maran.agent.v1.DbService/CreateDatabaseUser",
    "/maran.agent.v1.DbService/DropDatabase",
    "/maran.agent.v1.DbService/DropDatabaseUser",
    "/maran.agent.v1.DbService/RepairDatabaseGrants",
    "/maran.agent.v1.DbService/SetDatabasePassword",
    "/maran.agent.v1.DbService/SetDatabaseUserPrivileges",
    "/maran.agent.v1.DnsService/DeleteZone",
    "/maran.agent.v1.DnsService/DisableDns",
    "/maran.agent.v1.DnsService/EnableDns",
    "/maran.agent.v1.DnsService/PutZone",
    "/maran.agent.v1.FilesService/ChangePermissions",
    "/maran.agent.v1.FilesService/CopyEntry",
    "/maran.agent.v1.FilesService/CreateArchive",
    "/maran.agent.v1.FilesService/CreateDirectory",
    "/maran.agent.v1.FilesService/DeleteEntry",
    "/maran.agent.v1.FilesService/EmptyTrash",
    "/maran.agent.v1.FilesService/ExtractArchive",
    "/maran.agent.v1.FilesService/ForceDeleteEntry",
    "/maran.agent.v1.FilesService/MoveEntry",
    "/maran.agent.v1.FilesService/PurgeExpiredTrash",
    "/maran.agent.v1.FilesService/RestoreTrashEntry",
    "/maran.agent.v1.FilesService/TrashEntry",
    "/maran.agent.v1.FilesService/WriteFile",
    "/maran.agent.v1.FirewallService/AllowPort",
    "/maran.agent.v1.FirewallService/BanAddress",
    "/maran.agent.v1.FirewallService/DenyPort",
    "/maran.agent.v1.FirewallService/UnbanAddress",
    "/maran.agent.v1.FtpsService/CreateFtpsUser",
    "/maran.agent.v1.FtpsService/DeleteFtpsUser",
    "/maran.agent.v1.FtpsService/DisableFtps",
    "/maran.agent.v1.FtpsService/EnableFtps",
    "/maran.agent.v1.FtpsService/ReloadFtpsTls",
    "/maran.agent.v1.FtpsService/SetFtpsPassword",
    "/maran.agent.v1.MailService/ConfigureMailDomain",
    "/maran.agent.v1.MailService/DeleteQueuedMessage",
    "/maran.agent.v1.MailService/DisableMail",
    "/maran.agent.v1.MailService/EnableMail",
    "/maran.agent.v1.MailService/FlushMailQueue",
    "/maran.agent.v1.MailService/ReloadMailTls",
    "/maran.agent.v1.MailService/RemoveAccountMail",
    "/maran.agent.v1.MailService/RemoveMailDomain",
    "/maran.agent.v1.MailService/RemoveMailbox",
    "/maran.agent.v1.MailService/SetAccountMailSuspension",
    "/maran.agent.v1.MailService/SetMailAliases",
    "/maran.agent.v1.MailService/SetMailbox",
    "/maran.agent.v1.MailService/SetMailboxAutoresponder",
    "/maran.agent.v1.PhpService/ApplyPhpPoolSettings",
    "/maran.agent.v1.PhpService/InstallPhpVersion",
    "/maran.agent.v1.PhpService/SetPhpExtension",
    "/maran.agent.v1.SftpService/CreateSftpUser",
    "/maran.agent.v1.SftpService/DeleteSftpUser",
    "/maran.agent.v1.SftpService/EndSftpSessions",
    "/maran.agent.v1.SftpService/SetAccountLoginsLocked",
    "/maran.agent.v1.SftpService/SetSftpAuthorizedKeys",
    "/maran.agent.v1.SftpService/SetSftpPassword",
    "/maran.agent.v1.SitesService/CreateSite",
    "/maran.agent.v1.SitesService/DeleteSite",
    "/maran.agent.v1.SitesService/DisableSite",
    "/maran.agent.v1.SitesService/EnableSite",
    "/maran.agent.v1.SitesService/PruneSiteLogs",
    "/maran.agent.v1.SitesService/ReloadWebServer",
    "/maran.agent.v1.SitesService/UpdateSitePhpVersion",
    "/maran.agent.v1.SslService/GenerateSelfSigned",
    "/maran.agent.v1.SslService/InstallCertificate",
    "/maran.agent.v1.SslService/InstallPanelCertificate",
    "/maran.agent.v1.SslService/RemoveCertificate",
    "/maran.agent.v1.SslService/RemovePanelChallenge",
    "/maran.agent.v1.SslService/RestorePanelSelfSigned",
    "/maran.agent.v1.SslService/WritePanelChallenge",
    // Both moves of a backup between servers. ExportBackup changes nothing on the
    // host, but customer data leaving it is the event the trail exists for
    // (issue #127, decision D12).
    "/maran.agent.v1.TransferService/ExportBackup",
    "/maran.agent.v1.TransferService/ImportBackup",
];

#[cfg(test)]
#[path = "../tests/audit/mutating_rpcs_tests.rs"]
mod tests;
