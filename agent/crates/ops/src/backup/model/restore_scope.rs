//! What one restore replaces.

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::fs::relative_path::RelativePath;

/// What one restore replaces (issue #160).
///
/// A closed set, built by the service layer from validated names and never from
/// a caller's text, so a restore cannot be aimed at anything the wire's scope
/// message cannot name. There is no "some other part" case: a part of the
/// archive this area does not know how to place is refused by the pre-scan.
///
/// The partial scopes are **replace-within-scope**, exactly as the whole restore
/// is: what the archive holds for the scope replaces what is there, and nothing
/// outside the scope is read, dropped, renamed or written.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum RestoreScope {
    /// The account's whole home and every database the panel still knows it
    /// owns. The only scope before issue #160, and still what a request that
    /// names none means.
    Whole,

    /// One database, dropped and reloaded from its dump in the archive. No file
    /// is touched and no other database is read.
    Database(DatabaseName),

    /// One directory of the account's home (a site's document root), replaced
    /// by the archive's copy of it. No other file and no database is touched.
    ///
    /// A [`RelativePath`] and not a `PathBuf`, so its containment is a property
    /// of the type: every component is an ordinary entry name, which is what
    /// lets the swap descend by descriptor. The ops area does not know what a
    /// site is; the service layer derives this path from a validated domain.
    Directory(RelativePath),
}
