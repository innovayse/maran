//! A privilege a customer's login may hold on one of the customer's own databases.

/// One database-level privilege, and nothing wider.
///
/// **The set is closed on purpose, and the omissions are the point.** There is no
/// `GRANT OPTION`, `FILE`, `PROCESS`, `SUPER`, `RELOAD`, `SHUTDOWN` or
/// `REPLICATION` here, and no global privilege of any kind, so no request can name
/// one and no statement built from this type can grant one. A login holding
/// `GRANT OPTION` on a database could hand that database to a login the panel has
/// no row for; one holding `FILE` could read files as the database server.
/// Leaving them out of the type makes "the agent never grants these" a property of
/// the signature instead of a check somebody has to remember.
///
/// The variants are declared in the order a rendered privilege list uses, which is
/// also the order of [`Self::ALL`]; [`DatabasePrivilegeSet`](super::database_privilege_set::DatabasePrivilegeSet)
/// relies on it for a deterministic statement.
///
/// The wire spelling (`DatabasePrivilege` in `db.proto`) is mapped onto this type
/// in the agent's service layer, one `match`, so the numbers live in the contract
/// alone and not a second time here.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, PartialOrd, Ord)]
pub enum DatabasePrivilege {
    /// `SELECT`.
    Select,
    /// `INSERT`.
    Insert,
    /// `UPDATE`.
    Update,
    /// `DELETE`.
    Delete,
    /// `CREATE`.
    Create,
    /// `DROP`.
    Drop,
    /// `ALTER`.
    Alter,
    /// `INDEX`.
    Index,
    /// `REFERENCES`.
    References,
    /// `CREATE TEMPORARY TABLES`.
    CreateTemporaryTables,
    /// `LOCK TABLES`.
    LockTables,
    /// `CREATE VIEW`.
    CreateView,
    /// `SHOW VIEW`.
    ShowView,
    /// `CREATE ROUTINE`.
    CreateRoutine,
    /// `ALTER ROUTINE`.
    AlterRoutine,
    /// `EXECUTE`.
    Execute,
    /// `TRIGGER`.
    Trigger,
    /// `EVENT`.
    Event,
}

impl DatabasePrivilege {
    /// Every privilege of the closed set, in rendering order.
    pub const ALL: [Self; 18] = [
        Self::Select,
        Self::Insert,
        Self::Update,
        Self::Delete,
        Self::Create,
        Self::Drop,
        Self::Alter,
        Self::Index,
        Self::References,
        Self::CreateTemporaryTables,
        Self::LockTables,
        Self::CreateView,
        Self::ShowView,
        Self::CreateRoutine,
        Self::AlterRoutine,
        Self::Execute,
        Self::Trigger,
        Self::Event,
    ];

    /// The keyword the server reads this privilege as in `GRANT` and `REVOKE`.
    ///
    /// A constant per variant: this is the only place a privilege becomes SQL
    /// text, so a statement can only ever contain a keyword written here.
    #[must_use]
    pub const fn sql_keyword(self) -> &'static str {
        match self {
            Self::Select => "SELECT",
            Self::Insert => "INSERT",
            Self::Update => "UPDATE",
            Self::Delete => "DELETE",
            Self::Create => "CREATE",
            Self::Drop => "DROP",
            Self::Alter => "ALTER",
            Self::Index => "INDEX",
            Self::References => "REFERENCES",
            Self::CreateTemporaryTables => "CREATE TEMPORARY TABLES",
            Self::LockTables => "LOCK TABLES",
            Self::CreateView => "CREATE VIEW",
            Self::ShowView => "SHOW VIEW",
            Self::CreateRoutine => "CREATE ROUTINE",
            Self::AlterRoutine => "ALTER ROUTINE",
            Self::Execute => "EXECUTE",
            Self::Trigger => "TRIGGER",
            Self::Event => "EVENT",
        }
    }

    /// This privilege's position in [`Self::ALL`], which is also its bit in a
    /// [`DatabasePrivilegeSet`](super::database_privilege_set::DatabasePrivilegeSet).
    pub(crate) const fn position(self) -> u32 {
        self as u32
    }
}

#[cfg(test)]
#[path = "../../tests/validation/db/database_privilege_tests.rs"]
mod tests;
