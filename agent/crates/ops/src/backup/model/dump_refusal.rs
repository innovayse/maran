//! Why the restricted loader refused a dump before a byte of it reached the server.

/// The reason a dump of someone else's making was refused.
///
/// A closed set of facts about the dump's TEXT, not about its rows: the scanner
/// never keeps a statement, a name or a value it read, so there is nothing a
/// hostile dump could put into this type. It is `Copy` and carries no field for
/// that reason.
///
/// A refusal is final. The loader never retries a refused dump with more
/// privilege, and never rewrites the text to make it acceptable (the dump is
/// hashed, and a rewrite would destroy the digest check).
#[derive(Debug, Clone, Copy, PartialEq, Eq, thiserror::Error)]
#[non_exhaustive]
pub enum DumpRefusal {
    /// A command of the database client rather than a statement: a backslash
    /// command, or a statement whose first word is one of the client's own
    /// (`system`, `source`, `delimiter`, `tee`, ...).
    ///
    /// The one refusal that no database privilege could replace. The client
    /// runs as a root-started process and `system` hands a line to a shell, so
    /// the scanner is the only control against it.
    #[error("the dump contains a command of the database client")]
    ClientCommand,

    /// A statement whose kind a dump of tables and rows has no use for:
    /// `GRANT`, `CREATE USER`, `LOAD DATA`, `SELECT`, `INSTALL PLUGIN`, `FLUSH`
    /// and every other statement that is not on the closed list.
    #[error("the dump contains a statement that is not on the list a dump may carry")]
    ForbiddenStatement,

    /// A `DEFINER` clause. The loader's user cannot create an object owned by
    /// another account, and that user is dropped as soon as the load ends.
    #[error("the dump names a definer")]
    DefinerClause,

    /// A trigger, procedure, function, event or view. Each would be owned by the
    /// loader's temporary user, which no longer exists once the load ends, so the
    /// object would be created already broken. Refused rather than imported
    /// without it, pending the owner's decision on routines (spike S1).
    #[error("the dump creates a trigger, routine, event or view")]
    StoredProgram,

    /// A statement naming a database other than the one being loaded:
    /// `USE other`, `CREATE DATABASE other`, `DROP DATABASE other`.
    ///
    /// Qualified names inside other statements (`other`.`t`) are NOT looked for
    /// here; the temporary user holds privileges on one database only, and the
    /// server refuses them.
    #[error("the dump names a database other than the one being loaded")]
    OtherDatabase,

    /// A word that has no place in a dump: `OUTFILE`, `DUMPFILE`, `INFILE`,
    /// `LOAD_FILE`, `SLEEP`, `BENCHMARK`, `CONNECTION`, `DIRECTORY` or
    /// `NO_BACKSLASH_ESCAPES`.
    #[error("the dump contains a keyword that reads or writes outside the database")]
    ForbiddenKeyword,

    /// A `SET` statement that assigns anything other than a user variable or a
    /// session variable of the short closed list a dump sets, or that carries an
    /// expression.
    #[error("the dump sets a variable it may not set")]
    UnsafeSet,

    /// The text cannot be read to its end as statements: an unterminated quote
    /// or comment, a NUL byte outside a literal, a statement too long to
    /// classify, or a spelling the client and the server read two ways (a
    /// backslash in a backtick name, a control byte after `--`, a quote or `;`
    /// inside a `/*M!` comment).
    #[error("the dump is not well-formed")]
    Malformed,
}
