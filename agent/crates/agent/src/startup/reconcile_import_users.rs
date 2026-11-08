//! The startup removal of temporary database users an interrupted import left
//! behind.

use maran_ops::backup::{RestrictedDumpHost, reap_import_users};

/// Drops the temporary import users still on the database server, at startup,
/// and logs what it did.
///
/// # Why it exists
///
/// Loading an imported dump creates a user that holds every privilege on one
/// database and drops it afterwards. An agent killed between the two leaves the
/// user, and the password it was created with, on the server for good. This pass
/// drops every user of the exact temporary shape (`maran_imp_` and sixteen hex
/// digits) and nothing else, through the same host the loader uses. It needs no
/// state of its own: the server's user table is read as it is now.
///
/// # Why it never refuses the start
///
/// The trade `reconcile_web_tree` is decided by: this process is the operator's
/// only remote repair tool, and a leftover user is a risk to report, not a
/// reason to take every other rpc down. A listing that fails drops nothing, and
/// the failure is logged for the operator. A host with no database server
/// reaches the same branch, since the client cannot answer; the next start
/// tries again.
///
/// Answers how many users were dropped, or `None` when the pass failed, so a
/// test can observe the outcome without reading the log.
pub(crate) fn reconcile_import_users(host: &dyn RestrictedDumpHost) -> Option<usize> {
    match reap_import_users(host) {
        Ok(dropped) => {
            tracing::info!(
                dropped,
                "temporary import users left behind by an interrupted import were dropped"
            );
            Some(dropped)
        }
        Err(error) => {
            tracing::warn!(
                %error,
                "the temporary import users could not be reconciled with the database \
                 server, so one left behind by an interrupted import may remain"
            );
            None
        }
    }
}

#[cfg(test)]
#[path = "../tests/startup/reconcile_import_users_tests.rs"]
mod tests;
