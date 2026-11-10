//! One hosting account found on a source, complete.

use maran_agent_core::validation::system::name::AccountName;

use super::source_database_record::SourceDatabaseRecord;
use super::source_site_record::SourceSiteRecord;

/// One account of the source with everything the read found for it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SourceAccountRecord {
    /// The system user name.
    pub name: AccountName,
    /// The home directory on the source, display only.
    pub home: String,
    /// The size of the home in bytes, or `None` when it was not measured.
    pub disk_bytes: Option<u64>,
    /// The sites whose document root lies in the home.
    pub sites: Vec<SourceSiteRecord>,
    /// The databases the account owns.
    pub databases: Vec<SourceDatabaseRecord>,
    /// How many crontab entries the account has.
    pub cron_entry_count: u32,
    /// How many mailboxes were found under the home (not migrated).
    pub mailbox_count: u32,
}
