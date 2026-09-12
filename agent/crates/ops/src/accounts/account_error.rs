//! Failures of the account operations.

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::validation::system::name_error::NameError;

/// The most characters of a refusing tool's standard error the agent writes to
/// its own log.
///
/// Bounded rather than whole, because the length of what a spawned program
/// prints is not a number this agent chose. Characters and not bytes, so the
/// truncation cannot fall inside a UTF-8 sequence. The tools of this area print
/// one short sentence, so the ceiling is never reached in practice — it exists
/// so that the one that someday does not cannot put an unbounded string into a
/// log line.
const LOGGED_STDERR_CHARACTERS: usize = 512;

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

    /// Another operation for this account is already running on this host.
    ///
    /// The per-account lock (`crate::accounts::account_lock`) is taken without
    /// waiting, so an operation that would overlap a backup, a restore, an SFTP
    /// login creation or another deletion of the SAME account is refused rather
    /// than queued. Refusing is the design and not a limitation: a deletion that
    /// waited would hold an RPC open for the length of a twenty-gigabyte
    /// restore, and the panel retries.
    ///
    /// Carries no field a tool's output could go in, for the reason
    /// [`AccountError::CommandFailed`] carries none.
    #[error("another operation is already running for account '{username}'")]
    Busy {
        /// The account both operations name.
        username: String,
    },

    /// A system command exited non-zero.
    ///
    /// **Carries the program and its exit status, and no field a message can go
    /// in.** That is the point rather than an accident, and it is the shape
    /// [`crate::db::DbError`] was given for the same reason: this is the one
    /// `ops` area besides the database area whose tools are handed, or hand
    /// back, credential material — `getent shadow <account>` answers with the
    /// account's password hash, and `passwd -S` reports on the login's
    /// credential state. A shape that cannot carry a string cannot carry what
    /// such a tool printed, whichever stream it printed it on
    /// (rules/security.md item 8).
    ///
    /// The areas whose errors DO carry tool output — `sites` (`nginx -t`) and
    /// `firewall` (`nft`) — are not inconsistent with this. Their tools are
    /// never handed a credential and never read one, and their output is what
    /// an operator has to act on. The criterion is per area and is stated so
    /// the next area can be placed on the right side of it, rather than being
    /// left to whichever of the two shapes a new file was copied from.
    ///
    /// This variant used to carry the tool's trimmed standard error, which
    /// `services/accounts/account_status.rs` copied onto the wire in
    /// `tool_output` and which this `Display` put in `message` besides. No tool
    /// this area runs writes a credential to standard error today, so nothing
    /// leaked; the shape was one added spawn away from being able to.
    ///
    /// What an operator loses is the tool's own sentence, and what replaces it
    /// is `AccountError::command_failed` (crate-private) writing that sentence
    /// once to the agent's
    /// `tracing` output. The agent is root and its log is root's; the wire error
    /// is the panel's. The split puts the tool's words on the side that already
    /// has root.
    #[error("{program} failed with status {status}")]
    CommandFailed {
        /// The program that was run.
        program: String,
        /// Its exit status.
        status: i32,
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

    /// The account's sites could not be inspected, so nothing can be
    /// concluded about whether they are serving.
    ///
    /// Its own variant rather than a `CommandFailed`, and never folded into a
    /// success with an empty list: the caller of the suspension state uses the
    /// answer to decide whether it may report an account as suspended, and an
    /// account whose sites could not be looked at is precisely the one it must
    /// refuse to report on.
    #[error("the account's sites could not be inspected: {reason}")]
    SiteInspection {
        /// What the site area refused with.
        reason: String,
    },

    /// The account's crontab could not be read, so its cron cannot be reported
    /// on.
    ///
    /// Never folded into a count of zero. Zero entries and an unreadable
    /// crontab are the same number to any caller that guesses, and zero is the
    /// one that reads as "nothing is firing" — over a crontab that is.
    #[error("the account's crontab could not be inspected: {reason}")]
    CronInspection {
        /// What the cron area refused with.
        reason: String,
    },

    /// The account's SFTP logins could not be enumerated, or one of them could
    /// not be asked about.
    ///
    /// Its own variant rather than the [`Self::SftpRemoval`] that the blanket
    /// `From<SftpError>` produces, and so the conversion is written out at the
    /// call site instead of ridden on `?`: the same refusal from the same area
    /// means "the deletion did not happen" in one operation and "the state
    /// could not be observed" in this one, and an operator sent to the wrong
    /// one of those looks in the wrong place.
    #[error("the account's sftp logins could not be inspected: {reason}")]
    SftpInspection {
        /// What the SFTP area refused with.
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

    /// The account's FTPS logins, jail or bind mount could not be taken away,
    /// so the account has NOT been deleted.
    ///
    /// Its own variant beside [`Self::SftpRemoval`] rather than folded into it,
    /// and the two are not interchangeable to whoever reads one. They name two
    /// different daemons, two different jail roots under `/var/lib`, two
    /// different mount units and two different groups; an operator told "the
    /// sftp teardown refused" while a vsftpd jail is the thing still mounted
    /// looks under the wrong path and finds nothing wrong there.
    ///
    /// The mount is the sharpest half, exactly as it is for SFTP. A bind mount
    /// that survives the deletion is a mount of a home `userdel` is about to
    /// remove, into a jail nothing owns any more; the uninstaller refuses to
    /// remove the agent's state directories while any mount is left under them,
    /// and a re-created account of the same name would land in the old jail
    /// rather than a fresh one. An FTPS login that survives is worse still: it
    /// is a `--non-unique` passwd entry carrying the freed uid, in the group the
    /// FTPS PAM stack authorises, and `userdel` on the account does not touch
    /// it.
    #[error("the account's ftps logins could not be removed: {reason}")]
    FtpsRemoval {
        /// What the FTPS area refused with.
        reason: String,
    },
}

impl AccountError {
    /// Builds a [`Self::CommandFailed`] and writes the tool's own words to the
    /// agent's log on the way past.
    ///
    /// **The one place a refusing tool's standard error is read in this area**,
    /// and the reason it is a constructor rather than four `AccountError::…{}`
    /// literals: the value is being dropped here, so this is its last chance to
    /// be recorded, and a caller that built the variant by hand would drop it
    /// silently. One function also means one grep for "where does a tool's
    /// output go in the account area?".
    ///
    /// Logged once, at the point the value ceases to exist, which is the only
    /// boundary available to it (rules/rust.md "Logging": an error is logged
    /// once, at the boundary that handles it). It carries the program, the
    /// status and the trimmed, bounded stderr — no account name, because the
    /// caller's own span already carries the command and its correlation id,
    /// and no captured standard output, which in this area is a shadow entry.
    #[must_use]
    pub(crate) fn command_failed(program: &str, outcome: &CommandOutcome) -> Self {
        let stderr: String = outcome
            .stderr
            .trim()
            .chars()
            .take(LOGGED_STDERR_CHARACTERS)
            .collect();
        if !stderr.is_empty() {
            tracing::warn!(
                program = program,
                status = outcome.status,
                stderr = stderr.as_str(),
                "a system tool refused an account operation"
            );
        }

        Self::CommandFailed {
            program: program.to_owned(),
            status: outcome.status,
        }
    }
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

impl From<crate::ftps::FtpsError> for AccountError {
    /// Reports an FTPS resource the account still owns as a refusal to delete
    /// it, flattened for the same reason the three conversions above are.
    ///
    /// Written as its own `From` rather than reusing the SFTP one, so `?` in the
    /// deletion cascade cannot silently label an FTPS refusal as an SFTP one:
    /// the two areas have distinct error types, and the compiler picks the
    /// conversion by the type it is given.
    fn from(error: crate::ftps::FtpsError) -> Self {
        Self::FtpsRemoval {
            reason: error.to_string(),
        }
    }
}

impl From<crate::sites::SitesOpError> for AccountError {
    /// Reports a site that could not be read as a refusal to answer about the
    /// account's suspension state, flattened for the reason the three
    /// conversions above are: what a caller acts on is that the host could not
    /// be observed, not which of the site area's eleven variants it was.
    fn from(error: crate::sites::SitesOpError) -> Self {
        Self::SiteInspection {
            reason: error.to_string(),
        }
    }
}
