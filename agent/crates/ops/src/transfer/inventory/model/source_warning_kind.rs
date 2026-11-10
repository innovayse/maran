//! The kinds of warning an inventory read can raise.

/// What an inventory warning is about. A closed set: the panel renders each kind
/// from its own language table, so no sentence travels.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum SourceWarningKind {
    /// A name did not validate and the entry was skipped, never repaired.
    NameRefused,
    /// A virtual host file or entry could not be read, or belongs to no account.
    SiteUnreadable,
    /// A crontab could not be read.
    CronUnreadable,
    /// The databases could not be read.
    DatabasesUnreadable,
    /// A home could not be measured.
    DiskUnreadable,
    /// A bound was reached and the read is incomplete.
    Truncated,
    /// Mail was found and is not migrated.
    MailNotMigrated,
}
