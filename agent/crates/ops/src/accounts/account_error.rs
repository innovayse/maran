//! Failures of the account operations.

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::spawn_error::SpawnError;
use maran_agent_core::validation::system::managed_account_error::ManagedAccountError;
use maran_agent_core::validation::system::name_error::NameError;
use maran_distro::AdapterBinary;

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

    /// The account has no row in this host's local password database.
    ///
    /// Also what a name served only by a name service, and a name whose row is
    /// ambiguous (the same name twice), is reported as: neither is a user this
    /// panel created, so neither is one it will act on.
    #[error("account '{username}' was not found")]
    NotFound {
        /// The name that was looked up.
        username: String,
    },

    /// The name is spelled correctly but is not a hosting account this agent will
    /// act on as root: it is reserved, or its user has a system identity, or its
    /// home is not the managed home.
    ///
    /// Raised before any tool runs and before any database, login, crontab or
    /// pool is touched, so the refusal changed nothing. A refusal and not an
    /// absence: the user may well exist, which is the point.
    ///
    /// **The `Display` names no rule**, and neither does the wire message built
    /// from it, because saying which check failed describes the shape of the
    /// rules to whoever is probing them. `refusal` is for the agent's own log
    /// and for tests, and is deliberately left out of the sentence.
    #[error("account '{username}' is not a hosting account this panel manages")]
    NotManaged {
        /// The name that was refused.
        username: String,
        /// Which check refused it. Never shown to a caller.
        refusal: ManagedAccountError,
    },

    /// The user behind the name was replaced while a deletion waited on other
    /// work, so the deletion stopped before `userdel`.
    ///
    /// The account was resolved at the start and again immediately before the
    /// irreversible step, and the user id or group id differed: the original
    /// user was removed by hand and a different one given the name, and
    /// removing this one would delete somebody else's account and files.
    #[error("account '{username}' changed identity while the operation ran")]
    IdentityChanged {
        /// The name both resolutions looked up.
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

    /// A program ran past its deadline or past its output ceiling and was killed,
    /// together with its whole process group.
    ///
    /// **Not a verdict.** It says the agent stopped waiting, not what the program
    /// would have answered or whether it finished its work on the host after the
    /// kill, so it is never folded into [`Self::CommandFailed`] (a refusal), into
    /// [`Self::CommandUnavailable`] (the tool is missing) or into any of the
    /// removal variants, whose meaning is "the account still owns the resource".
    /// An operation that was killed mid-way may have done its work; the caller
    /// reads the account again before it believes either outcome.
    ///
    /// Carries the program's path and the bound, and never a byte the program
    /// printed (rules/security.md item 8).
    #[error(transparent)]
    Bound(#[from] BoundExceeded),

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

    /// The password database or a home directory's own metadata could not be
    /// read, so `RepairHomeGroups` cannot classify what it examined.
    ///
    /// Its own variant rather than [`Self::CommandFailed`], because nothing
    /// was run and refused — the failure is a filesystem read, and the two
    /// carry different information to whoever reads them. Never folded into
    /// a report as if nothing needed fixing: an account this repair could not
    /// even inspect is not the same as one whose home is already correct.
    #[error("could not inspect a home directory or the password database: {reason}")]
    HomeInspection {
        /// What could not be read.
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

    /// Processes were still running as the account's uid after every round of
    /// the cull, so the deletion stopped before `userdel`.
    ///
    /// `userdel` is NOT the guard against this: it refuses a user with live
    /// processes only by reading `/proc/<pid>/root`, which the kernel allows for
    /// another uid's process only to a caller holding `CAP_SYS_PTRACE`, and this
    /// agent's bounding set deliberately lacks it. Run anyway, `userdel` exits 0,
    /// the uid is freed, and the next `useradd` hands it to a new tenant who then
    /// owns the survivor. So the survivor is refused here, while the account
    /// still exists.
    ///
    /// The account is half-deleted RECOVERABLY: its databases, logins, crontab
    /// and pools are gone and the user, its uid and its home remain. The same
    /// deletion repeated converges once the survivor has exited (rules/rust.md
    /// "Idempotency"). Carries the name only: no pid, command line or count
    /// reaches a customer-facing message; the operator-side detail is the log
    /// line the deletion writes.
    #[error("processes of account '{username}' are still running after being killed")]
    ProcessesRemain {
        /// The account whose processes survived.
        username: String,
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

impl AccountError {
    /// Folds a failed spawn of `program` into the account error surface.
    ///
    /// The one home of the mapping `SystemHost::run` needs: a bound the core
    /// enforced ([`SpawnError::Bound`]) passes through as [`Self::Bound`], so a
    /// program that was killed is never reported as one that is "unavailable";
    /// every other failure is [`Self::CommandUnavailable`] naming `program` and the
    /// core's reason. The core's error does not carry the program, which is why the
    /// caller supplies it.
    pub(crate) fn from_spawn(program: &AdapterBinary, error: SpawnError) -> Self {
        match error {
            SpawnError::Bound(bound) => Self::Bound(bound),
            other => Self::CommandUnavailable {
                program: program.to_string(),
                reason: other.to_string(),
            },
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
    ///
    /// A bound is the one exception and stays a bound: a pool removal that was
    /// killed at its deadline has not established that the account still owns
    /// the pool, and `PoolRemoval` says exactly that.
    fn from(error: crate::php::PhpOpError) -> Self {
        match error {
            crate::php::PhpOpError::Bound(bound) => Self::Bound(bound),
            other => Self::PoolRemoval {
                reason: other.to_string(),
            },
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
    ///
    /// A bound stays a bound, for the reason the PHP conversion gives.
    fn from(error: crate::db::DbError) -> Self {
        match error {
            crate::db::DbError::Bound(bound) => Self::Bound(bound),
            other => Self::DatabaseRemoval {
                reason: other.to_string(),
            },
        }
    }
}

impl From<crate::sftp::SftpError> for AccountError {
    /// Reports an SFTP resource the account still owns as a refusal to delete
    /// it, flattened for the same reason the two conversions above are.
    ///
    /// A bound stays a bound, for the reason the PHP conversion gives.
    fn from(error: crate::sftp::SftpError) -> Self {
        match error {
            crate::sftp::SftpError::Bound(bound) => Self::Bound(bound),
            other => Self::SftpRemoval {
                reason: other.to_string(),
            },
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
    ///
    /// A bound stays a bound, for the reason the PHP conversion gives.
    fn from(error: crate::ftps::FtpsError) -> Self {
        match error {
            crate::ftps::FtpsError::Bound(bound) => Self::Bound(bound),
            other => Self::FtpsRemoval {
                reason: other.to_string(),
            },
        }
    }
}

impl From<crate::sites::SitesOpError> for AccountError {
    /// Reports a site that could not be read as a refusal to answer about the
    /// account's suspension state, flattened for the reason the three
    /// conversions above are: what a caller acts on is that the host could not
    /// be observed, not which of the site area's eleven variants it was.
    ///
    /// A bound stays a bound: a site read that was killed at its deadline is not
    /// a statement that the sites could not be inspected.
    fn from(error: crate::sites::SitesOpError) -> Self {
        match error {
            crate::sites::SitesOpError::Bound(bound) => Self::Bound(bound),
            other => Self::SiteInspection {
                reason: other.to_string(),
            },
        }
    }
}

#[cfg(test)]
#[path = "../tests/accounts/account_error_tests.rs"]
mod tests;
