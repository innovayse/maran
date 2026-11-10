//! An account while its inventory is being filled in.

use super::passwd_entry::PasswdEntry;
use super::source_account_record::SourceAccountRecord;
use super::source_database_record::SourceDatabaseRecord;
use super::source_site_record::SourceSiteRecord;

/// One account between the password database and the moment it is sent.
///
/// Every step of the read adds its part; nothing is sent until the last step
/// has run, so an account always leaves the agent complete.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AccountDraft {
    /// The account as the password database listed it.
    pub entry: PasswdEntry,
    /// The sites attributed to it.
    pub sites: Vec<SourceSiteRecord>,
    /// The databases attributed to it.
    pub databases: Vec<SourceDatabaseRecord>,
    /// The crontab entries counted.
    pub cron_entry_count: u32,
    /// The mailboxes counted.
    pub mailbox_count: u32,
    /// The measured size of the home, when it was.
    pub disk_bytes: Option<u64>,
}

impl AccountDraft {
    /// An account with nothing found for it yet.
    #[must_use]
    pub fn new(entry: PasswdEntry) -> Self {
        Self {
            entry,
            sites: Vec::new(),
            databases: Vec::new(),
            cron_entry_count: 0,
            mailbox_count: 0,
            disk_bytes: None,
        }
    }

    /// The finished account.
    #[must_use]
    pub fn into_record(self) -> SourceAccountRecord {
        SourceAccountRecord {
            name: self.entry.name,
            home: self.entry.home.as_str().to_owned(),
            disk_bytes: self.disk_bytes,
            sites: self.sites,
            databases: self.databases,
            cron_entry_count: self.cron_entry_count,
            mailbox_count: self.mailbox_count,
        }
    }
}

#[cfg(test)]
#[path = "../../../tests/transfer/inventory/model/account_draft_tests.rs"]
mod tests;
