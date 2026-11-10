//! Which account each database belongs to.

use super::source_database_record::SourceDatabaseRecord;

/// The result of attributing databases to accounts.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DatabaseAssignment {
    /// The databases of each account, in the order of the accounts passed in.
    pub per_account: Vec<Vec<SourceDatabaseRecord>>,
    /// How many databases carry no account's prefix, or a name the database-name
    /// validator refuses for the account whose prefix they carry.
    pub refused: usize,
}
