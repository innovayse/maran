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
    "/maran.agent.v1.DbService/DropDatabase",
    "/maran.agent.v1.DbService/RepairDatabaseGrants",
    "/maran.agent.v1.DbService/SetDatabasePassword",
    "/maran.agent.v1.FilesService/ChangePermissions",
    "/maran.agent.v1.FilesService/CreateArchive",
    "/maran.agent.v1.FilesService/CreateDirectory",
    "/maran.agent.v1.FilesService/DeleteEntry",
    "/maran.agent.v1.FilesService/ExtractArchive",
    "/maran.agent.v1.FilesService/MoveEntry",
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
    "/maran.agent.v1.PhpService/InstallPhpVersion",
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
    "/maran.agent.v1.SitesService/ReloadWebServer",
    "/maran.agent.v1.SitesService/UpdateSitePhpVersion",
    "/maran.agent.v1.SslService/GenerateSelfSigned",
    "/maran.agent.v1.SslService/InstallCertificate",
    "/maran.agent.v1.SslService/InstallPanelCertificate",
    "/maran.agent.v1.SslService/RemoveCertificate",
    "/maran.agent.v1.SslService/RemovePanelChallenge",
    "/maran.agent.v1.SslService/RestorePanelSelfSigned",
    "/maran.agent.v1.SslService/WritePanelChallenge",
];

#[cfg(test)]
#[path = "../tests/audit/mutating_rpcs_tests.rs"]
mod tests;
