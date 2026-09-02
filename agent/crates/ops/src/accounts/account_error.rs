//! Failures of the account operations.

use maran_agent_core::validation::system::name_error::NameError;

/// What can go wrong while managing an account's operating-system identity.
#[derive(Debug, thiserror::Error)]
#[non_exhaustive]
pub enum AccountError {
    /// The name is not one this agent will turn into a system user.
    ///
    /// Raised by the agent's own revalidation, not by the API's: a name reaching
    /// here becomes a user, a home directory and a path segment, so it is checked
    /// where it is used rather than where it was received.
    #[error("invalid account name")]
    InvalidName(#[from] NameError),

    /// The account already exists on this host.
    #[error("account '{username}' already exists")]
    AlreadyExists {
        /// The name that was asked for.
        username: String,
    },

    /// The account does not exist on this host.
    #[error("account '{username}' was not found")]
    NotFound {
        /// The name that was looked up.
        username: String,
    },

    /// A system command exited non-zero.
    ///
    /// Carries the program and its stderr because an operator reading the agent's
    /// log needs to know which tool refused and why; the text never reaches a
    /// customer (rules/security.md item 8).
    #[error("{program} failed with status {status}: {stderr}")]
    CommandFailed {
        /// The program that was run.
        program: String,
        /// Its exit status.
        status: i32,
        /// Its standard error, trimmed.
        stderr: String,
    },

    /// A system command could not be run at all — usually because it is not installed.
    #[error("could not run {program}: {reason}")]
    CommandUnavailable {
        /// The program that could not be started.
        program: String,
        /// Why it could not be started.
        reason: String,
    },

    /// A command's output did not have the shape this agent knows how to read.
    #[error("could not read the output of {program}")]
    UnreadableOutput {
        /// The program whose output could not be parsed.
        program: String,
    },

    /// One of the account's php-fpm pools could not be taken away, so the
    /// account has NOT been deleted.
    ///
    /// Its own variant rather than a `CommandFailed`, because the two mean
    /// opposite things to whoever reads them. A refused `userdel` is an account
    /// that is still there and still works. A refused pool removal is an
    /// account that is still there ON PURPOSE — the deletion stopped rather
    /// than leave behind a pool naming a user about to vanish, which is what
    /// makes the next reload take PHP down for every tenant on the server.
    #[error("the account's php-fpm pools could not be removed: {reason}")]
    PoolRemoval {
        /// What the PHP area refused with.
        reason: String,
    },

    /// The account's databases could not be taken away, so the account has NOT
    /// been deleted.
    ///
    /// Its own variant for the same reason [`Self::PoolRemoval`] is: what an
    /// operator must act on is that the deletion stopped on purpose. A database
    /// left behind when an account of the same name is created again is that
    /// customer's live data handed to the next tenant, together with the
    /// credential that reaches it — which no later operation can undo, whereas
    /// an account that is still there can simply be deleted again.
    #[error("the account's databases could not be removed: {reason}")]
    DatabaseRemoval {
        /// What the database area refused with.
        reason: String,
    },

    /// The account's SFTP logins, jail or bind mount could not be taken away,
    /// so the account has NOT been deleted.
    ///
    /// The mount is the sharpest half. A bind mount that survives the deletion
    /// is a mount of a home `userdel` is about to remove, into a jail nothing
    /// owns any more; the uninstaller refuses to remove the agent's state
    /// directory while any mount is left under it, and a re-created account of
    /// the same name would land in the old jail rather than a fresh one.
    #[error("the account's sftp logins could not be removed: {reason}")]
    SftpRemoval {
        /// What the SFTP area refused with.
        reason: String,
    },
}

impl From<crate::php::PhpOpError> for AccountError {
    /// Reports a pool the account still owns as a refusal to delete the account.
    ///
    /// Deliberately flattens the PHP area's variants into one sentence rather
    /// than re-exporting them: what an operator has to act on here is that the
    /// deletion did not happen and why, not which of six PHP failure modes it
    /// was — and a caller matching on the PHP area's variants through the
    /// account area's error would be reaching across an area boundary
    /// (rules/rust.md "one error enum per area").
    fn from(error: crate::php::PhpOpError) -> Self {
        Self::PoolRemoval {
            reason: error.to_string(),
        }
    }
}

impl From<crate::db::DbError> for AccountError {
    /// Reports a database the account still owns as a refusal to delete it.
    ///
    /// Flattened into one sentence rather than re-exported, for the reason the
    /// PHP conversion above gives: a caller matching on the database area's
    /// variants through the account area's error would be reaching across an
    /// area boundary, and what an operator has to act on is that the deletion
    /// did not happen and why.
    fn from(error: crate::db::DbError) -> Self {
        Self::DatabaseRemoval {
            reason: error.to_string(),
        }
    }
}

impl From<crate::sftp::SftpError> for AccountError {
    /// Reports an SFTP resource the account still owns as a refusal to delete
    /// it, flattened for the same reason the two conversions above are.
    fn from(error: crate::sftp::SftpError) -> Self {
        Self::SftpRemoval {
            reason: error.to_string(),
        }
    }
}
