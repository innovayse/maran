//! One account whose home was, or would be, re-grouped for the web server.

use maran_agent_core::validation::system::name::AccountName;

/// An account whose home the repair narrowed — or would narrow — to the web
/// server's group.
///
/// Carries an [`AccountName`] rather than a plain string because a row only
/// reaches this struct after its recorded home matched the exact path this
/// agent creates homes at and every other check
/// (`AccountOperations::repair_home_groups`'s doc comment) passed: the account
/// name is not merely well-shaped, it is the account whose home this is.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RepairedHome {
    /// The account whose home was re-grouped.
    pub account: AccountName,

    /// The home directory that was, or would be, re-grouped.
    ///
    /// Always `<home root>/<account>` — carried anyway, rather than
    /// recomputed by every reader, because a report is read by a caller that
    /// should not have to reconstruct the path from the name to say what
    /// changed.
    pub home: String,
}
