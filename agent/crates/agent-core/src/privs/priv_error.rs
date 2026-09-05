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
    /// The account exists but its primary group is gid 0, so running "as the
    /// customer" would run in root's group. Refused rather than obeyed.
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
    /// One of `setgroups`, `setgid` or `setuid` returned an error in the child.
    /// The child exited without doing any work.
    #[error("privilege drop failed in child")]
    DropFailed,
    /// The child dropped, re-read its own credentials, and found at least one of
    /// them not to be what it asked for. A partially applied drop: the process
    /// looked unprivileged and was not, so it exited before touching anything.
    #[error("privilege drop verification failed in child")]
    VerificationFailed,
    /// The work closure ran as the account and returned an error. Which error is
    /// deliberately not carried across: the child cannot hand a Rust value back
    /// through an exit status, and inventing a channel to do so would widen this
    /// module's surface for no security gain.
    #[error("work failed while running as the account")]
    WorkFailed,
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
}
