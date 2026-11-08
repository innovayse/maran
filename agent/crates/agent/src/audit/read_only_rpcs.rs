//! The closed list of agent rpcs that yield no audit line.

/// Every agent rpc that only reports state, as the full gRPC path the request
/// carries (`/maran.agent.v1.<Service>/<Method>`).
///
/// An rpc is here when its doc comment in `proto/agent/v1/` says it reads and
/// changes nothing on the host: every `List*`, `Get*` and `Tail*`, `SearchEntries`,
/// and two that are easy to misfile. `VerifyBackup` reads one artifact and its
/// sidecar back and extracts nothing into a home. `ProbeDestination` is filed
/// here because the agent that ships today performs no probe and answers
/// `NOT_IMPLEMENTED` for every destination; the day it writes its canary object
/// it changes a remote store and moves to
/// `MUTATING_RPCS` in that change.
///
/// Read-only rpcs yield no audit line by design: the trail covers what the
/// agent changed, not what it disclosed. The one exception is an rpc that hands
/// back the CONTENTS of a customer's file, which has a list of its own
/// (`DISCLOSING_RPCS`). Sorted in byte order and free of
/// duplicates, as the census test in `src/tests/audit/` checks.
pub const READ_ONLY_RPCS: &[&str] = &[
    "/maran.agent.v1.AccountsService/GetAccountSuspensionState",
    "/maran.agent.v1.AccountsService/GetAccountUsage",
    "/maran.agent.v1.BackupService/ListBackups",
    "/maran.agent.v1.BackupService/ProbeDestination",
    "/maran.agent.v1.BackupService/VerifyBackup",
    "/maran.agent.v1.CronService/GetCronEntryOutput",
    "/maran.agent.v1.CronService/GetCronEnvironment",
    "/maran.agent.v1.CronService/ListCronEntries",
    "/maran.agent.v1.DbService/GetDatabaseSize",
    "/maran.agent.v1.DbService/ListDatabases",
    "/maran.agent.v1.DnsService/GetDnsStatus",
    "/maran.agent.v1.DnsService/GetZone",
    "/maran.agent.v1.DnsService/ListZones",
    "/maran.agent.v1.FilesService/ListDirectory",
    "/maran.agent.v1.FilesService/ListTrash",
    "/maran.agent.v1.FilesService/SearchEntries",
    "/maran.agent.v1.FirewallService/ListBans",
    "/maran.agent.v1.FirewallService/ListRules",
    "/maran.agent.v1.FtpsService/GetFtpsStatus",
    "/maran.agent.v1.MailService/GetMailStatus",
    "/maran.agent.v1.MailService/ListMailQueue",
    "/maran.agent.v1.MailService/ListMailboxUsage",
    "/maran.agent.v1.MonitorService/GetAccountsDiskUsage",
    "/maran.agent.v1.MonitorService/GetClockSync",
    "/maran.agent.v1.MonitorService/GetHostMetrics",
    "/maran.agent.v1.MonitorService/GetQuotaEnforceability",
    "/maran.agent.v1.MonitorService/GetRebootRequirement",
    "/maran.agent.v1.MonitorService/GetServerFingerprintInputs",
    "/maran.agent.v1.MonitorService/GetServiceStatuses",
    "/maran.agent.v1.MonitorService/GetSftpJailStatus",
    "/maran.agent.v1.MonitorService/ListPendingUpdates",
    "/maran.agent.v1.PhpService/ListInstallablePhpVersions",
    "/maran.agent.v1.PhpService/ListPhpExtensions",
    "/maran.agent.v1.PhpService/ListPhpVersions",
    "/maran.agent.v1.SitesService/SummariseSiteTraffic",
    "/maran.agent.v1.SitesService/TailSiteLog",
    "/maran.agent.v1.SystemService/GetAgentInfo",
];

#[cfg(test)]
#[path = "../tests/audit/read_only_rpcs_tests.rs"]
mod tests;
