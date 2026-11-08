//! Why privileged work on a customer's behalf could not be done.

/// Failures of account-id resolution and of the drop-privileges fork.
///
/// The variants distinguish *where* the sequence stopped, because the security
/// meaning differs sharply: [`PrivError::VerificationFailed`] means the kernel
/// accepted a drop that did not fully apply and the child refused to continue,
/// which is a far more serious signal than a plain [`PrivError::DropFailed`].
///
/// Text here is for the operator log. The customer-facing wording is produced by
/// the C# side from the variant (rules/rust.md "Errors"), so nothing in these
/// messages carries a path, a uid, or tool output.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum PrivError {
    /// No entry for the requested account in the system's user database.
    #[error("no such account")]
    NoSuchAccount,
    /// The user database could not be read at all — not the same as the account
    /// being absent, and never treated as "the account does not exist".
    #[error("account lookup failed: errno {errno}")]
    LookupFailed {
        /// The `errno` `getpwnam_r` reported.
        errno: i32,
    },
    /// The account exists but resolves to uid 0, so running "as the customer"
    /// would run as root. Refused rather than obeyed.
    #[error("account resolves to the root user")]
    RootAccount,
    /// A group that had to be entered or applied resolves to gid 0.
    ///
    /// Two callers reach it, and both are refusals of the same shape. An
    /// account whose primary group is gid 0 would make running "as the
    /// customer" run in root's group; a web server group that resolves to gid 0
    /// would make a restored home group-owned by root
    /// ([`GroupId::resolve`](crate::privs::group_id::GroupId::resolve)).
    ///
    /// Separate from [`PrivError::RootAccount`] because the drop would otherwise
    /// *succeed*: the child would verify correctly, having received exactly the
    /// gid it asked for. Verification confirms fidelity, not safety, so the
    /// safety question is answered here instead.
    #[error("account resolves to the root group")]
    RootGroup,
    /// The account exists but its uid or gid is below the host's threshold for a
    /// human account, which makes it a system account (`daemon`, `bin`, `mail`,
    /// `postgres`, `nginx`) rather than a hosting one.
    ///
    /// `AccountName::parse` accepts any 3-30 character lowercase name, so every
    /// one of those is a syntactically valid request. Running as `postgres` is
    /// not a privilege escalation to root, but it is a lateral move onto a
    /// service identity the panel has no business acting as.
    #[error("account is a system account, not a hosting account")]
    SystemAccount,
    /// `fork` itself failed; no child exists and no work was done.
    #[error("fork failed: errno {errno}")]
    ForkFailed {
        /// The `errno` `fork` reported.
        errno: i32,
    },
    /// No entry for the requested group in the system's group database.
    ///
    /// Its own variant beside [`PrivError::NoSuchAccount`] rather than shared
    /// with it, because the two send an operator to different files: a missing
    /// hosting account is a panel-state problem, and a missing web server group
    /// is a host whose web server was removed or renamed under the agent.
    #[error("no such group")]
    NoSuchGroup,
    /// The group database could not be read at all — not the same as the group
    /// being absent, and never treated as "the group does not exist".
    #[error("group lookup failed: errno {errno}")]
    GroupLookupFailed {
        /// The `errno` `getgrnam_r` reported.
        errno: i32,
    },
    /// One of `setgroups`, `setgid` or `setuid` returned an error in the child.
    /// The child exited without doing any work.
    ///
    /// The syscall is NAMED, and the name is the whole point of the variant carrying a field at
    /// all: "privilege drop failed in child" was the message for a year, and when it finally
    /// fired on a real server (issue #81) it could not distinguish three different failures with
    /// three different causes. Three hypotheses were tested from the outside and all three were
    /// wrong, because the error named a category instead of a call.
    ///
    /// A `&'static str` rather than an enum: the three names are literals this module writes and
    /// nothing branches on them — they are read by a person, in a log line, while reproducing the
    /// failure by hand.
    #[error("privilege drop failed in child: {syscall}")]
    DropFailed {
        /// Which of the three calls returned an error: `setgroups`, `setgid` or `setuid`.
        syscall: &'static str,
    },
    /// The child dropped, re-read its own credentials, and found at least one of
    /// them not to be what it asked for. A partially applied drop: the process
    /// looked unprivileged and was not, so it exited before touching anything.
    #[error("privilege drop verification failed in child")]
    VerificationFailed,
    /// The work closure ran as the account and returned an error. Which error is
    /// deliberately not carried across: the child cannot hand a Rust value back
    /// through an exit status, and a channel for it would widen this module's
    /// surface for no security gain. The two entry points that DO have a channel
    /// (`fork_as_account_for_bytes`, `fork_as_account_for_descriptor`) carry data
    /// in it, never an error: a child that fails is still reported by its exit
    /// status, as this variant.
    #[error("work failed while running as the account")]
    WorkFailed,
    /// The work closure ran as the account and found the one entry it was asked to open absent
    /// (`ENOENT` on the final `openat`). Issue #455.
    ///
    /// The only refusal of the work that crosses the exit status with a meaning of its own, and
    /// it carries nothing: not the name, not the errno. A closure asks for it by returning this
    /// variant; the child turns it into its own closed exit status, and every other error a
    /// closure returns is still [`PrivError::WorkFailed`]. A directory, a symlink, a hard link
    /// or another owner's file therefore stay indistinguishable refusals, exactly as before.
    #[error("the entry the work asked for does not exist")]
    NoSuchEntry,
    /// The child was killed by a signal — an OOM kill, an operator `kill`, a
    /// crash — so the work may have been applied in part.
    #[error("child terminated by signal {signal}")]
    ChildSignalled {
        /// The signal number that terminated the child.
        signal: i32,
    },
    /// The child was still running when the parent's patience ran out, so it was
    /// killed and reaped. The work may have been applied in part.
    ///
    /// Distinct from [`PrivError::ChildSignalled`] even though the child did die
    /// of a signal: this one was sent by us, and it means the child was wedged —
    /// a lock frozen at fork time, a `work` closure that never returns — which is
    /// a defect in the agent, where an outside `kill` is an event on the host.
    #[error("child did not finish in time and was killed")]
    ChildTimedOut,
    /// The child outlasted the parent's patience, was killed, and was still not
    /// collectable when the reap's own ceiling ran out. The process may still
    /// exist at the customer's uid.
    ///
    /// Distinct from [`PrivError::ChildTimedOut`], which is the ordinary end of
    /// the same path: there the kill worked and nothing is left behind. This one
    /// says the recovery itself did not complete — an operator has a stray
    /// process to look at — and it exists so that the reap can have a ceiling at
    /// all. A reap that blocked until the child died would report `ChildTimedOut`
    /// or hang forever, and a hang is the one outcome nobody can act on.
    #[error("child was killed but could not be collected")]
    ChildNotCollected,
    /// `waitpid` failed, so the child's outcome is unknown.
    #[error("waiting for child failed: errno {errno}")]
    WaitFailed {
        /// The `errno` `waitpid` reported.
        errno: i32,
    },
    /// The wait status says the child neither exited nor was killed — it was
    /// stopped or traced, which nothing this module forks can do to itself.
    ///
    /// Separate from [`PrivError::UnexpectedExit`] because the number means
    /// something different: this carries the RAW wait status, that one carries an
    /// exit code. One variant for both would print the same sentence for a child
    /// that exited 19 and for one that never exited at all.
    #[error("child neither exited nor was killed: raw wait status {status}")]
    ChildDidNotExit {
        /// The raw wait status `waitpid` wrote.
        status: i32,
    },
    /// The child exited with a status this module never produces, which means
    /// something inside it exited on a path that was not meant to exist.
    #[error("child exited with unexpected status {status}")]
    UnexpectedExit {
        /// The exit code the child returned, as `WEXITSTATUS` reports it — never
        /// a raw wait status; see [`PrivError::ChildDidNotExit`].
        status: i32,
    },
    /// The child refused the work with a number from the caller's own closed list.
    ///
    /// Only [`fork_reporting_as_account`](super::fork_reporting_as_account::fork_reporting_as_account)
    /// produces it: the child's exit status said "the work failed" and its report named why, as
    /// a value in `1..=63`. This module does not interpret the number; the caller that chose the
    /// list turns it back into its own typed error, and reads one outside its list as a plain
    /// failure.
    #[error("the child refused the work: reason {reason}")]
    WorkRefused {
        /// The caller-chosen refusal number, `1..=63`.
        reason: u8,
    },
    /// The child finished but its report was not the fixed shape: too short, or a refusal
    /// number outside `1..=63`, or a success that carried a refusal. Never trusted.
    #[error("the child's report was malformed")]
    ReportMalformed,
    /// The child wrote more bytes than the caller allowed it to bring back, so it was
    /// killed and reaped and nothing it wrote is returned.
    ///
    /// Its own variant, not [`PrivError::WorkFailed`]: the child did not fail, the PARENT
    /// refused its result. The difference is what an operator does next — a child that fails
    /// is a bug or a hostile tree, a result that is too large is a directory or a search that
    /// outgrew its bound, and the caller maps the two to different answers for the customer.
    #[error("the child's result exceeded the allowed size")]
    ResultTooLarge,
    /// The channel between parent and child could not be made or read: `pipe`, `socketpair`,
    /// `poll`, `read`, `recvmsg` or `fcntl` failed in the parent.
    ///
    /// Separate from [`PrivError::WaitFailed`] because the child's outcome may be perfectly
    /// known; it is what the child brought, or whether it could, that is not.
    #[error("channel to the child failed: errno {errno}")]
    ChannelFailed {
        /// The `errno` the failing call reported.
        errno: i32,
    },
    /// The child exited successfully without handing over exactly one descriptor: none, more
    /// than one, or a message the kernel had to truncate. Everything that did arrive was
    /// closed.
    #[error("the child did not hand over exactly one descriptor")]
    DescriptorNotReceived,
    /// The child handed over a descriptor that could write. The hand-off exists so the root
    /// parent can read a customer's file; a descriptor that can also modify it is refused and
    /// closed, whatever the child meant by it.
    #[error("the descriptor handed over is not read-only")]
    DescriptorNotReadOnly,
}

impl PrivError {
    /// The channel failure for an I/O error raised on the parent's side of a channel to the child.
    ///
    /// One constructor for the three places that need it (the byte channel's pipe and read, the
    /// descriptor channel's socket pair and poll, the receive) so that "which errno does a failed
    /// channel report" has one answer. An error with no OS code reports 0, as the other `errno`
    /// fields of this enum do when the standard library had none to give.
    pub(super) fn channel_failed(error: &std::io::Error) -> Self {
        Self::ChannelFailed {
            errno: error.raw_os_error().unwrap_or(0),
        }
    }
}
