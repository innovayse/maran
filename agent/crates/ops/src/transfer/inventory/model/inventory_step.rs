//! The steps of an inventory read.

/// A step of the read, in the order they run.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum InventoryStep {
    /// Reading the list of system accounts.
    Accounts,
    /// Reading the web server's virtual hosts.
    Sites,
    /// Reading the database server's databases.
    Databases,
    /// Reading the accounts' crontabs.
    Cron,
    /// Measuring the accounts' homes.
    Disk,
}
