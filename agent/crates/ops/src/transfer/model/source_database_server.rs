//! The database server found on a source.

/// A database server a probe recognised. "None found, or the probe could not
/// run" is `None` in an `Option`, not a value of this enum.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SourceDatabaseServer {
    /// MariaDB.
    Mariadb,
    /// MySQL.
    Mysql,
}
