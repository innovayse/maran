//! The seam between the firewall operations and the machine they run on.

use std::path::{Path, PathBuf};

use maran_agent_core::command_outcome::CommandOutcome;

use crate::firewall::firewall_error::FirewallError;

/// The operating-system operations the firewall area needs.
///
/// A trait rather than direct calls to `std::process::Command` and `std::fs`,
/// and not for abstraction's sake: every method here either loads a ruleset
/// into a running kernel or replaces the file that ruleset is rebuilt from,
/// which is exactly what a unit test must never really do. Behind this seam
/// the decisions worth reviewing — which `nft` subcommand, in which argument
/// order, what each exit status means, and above all the ORDER of check,
/// flush and load — are testable against a fake, and the one implementation
/// that really touches the machine stays small enough to read in full.
///
/// The write half is deliberately split into four methods rather than one
/// `write_file`. The apply protocol's whole safety property is its ORDER —
/// stage, check, flush, rename, load — and an order is only testable when
/// each step is a separate observable call. A single `write_file` that did
/// the first four would make the one thing this area must not get wrong the
/// one thing no test could see.
///
/// Implementations MUST spawn with an argv array against an absolute path
/// taken from the `DistroAdapter`, never through a shell and never through a
/// program name resolved by `PATH` (rules/security.md item 3).
///
/// Every method here MUST be reached from `tokio::task::spawn_blocking`: each
/// one waits for a process or for the disk, and a blocking call on a runtime
/// worker stalls every other in-flight command (rules/rust.md "Async and
/// blocking"). That is a requirement on whoever calls this area's operations,
/// which is where every call into this trait comes from — see the module
/// documentation, which states it in full and names what enforces it.
pub trait FirewallHost: Send + Sync {
    /// Runs `program` with `arguments` as an argv array and waits for it.
    ///
    /// # Errors
    ///
    /// Returns [`FirewallError::NftFailed`] when the program cannot be started
    /// or cannot be waited for. A non-zero exit is NOT an error here — it is
    /// returned in the outcome, because each caller reads a status
    /// differently: `nft list table` exiting non-zero means "the table is not
    /// there", while `nft -f` exiting non-zero means the apply failed.
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, FirewallError>;

    /// Reads `path` as UTF-8 text, answering `None` when it is not there.
    ///
    /// "Not there" is not a failure: a host whose ruleset file has never been
    /// written has an empty rule set, which is an ordinary state and the one
    /// every host is in before the installer seeds it.
    ///
    /// # Errors
    ///
    /// Returns [`FirewallError::RulesetUnreadable`] when the file exists and
    /// cannot be read, or holds bytes that are not UTF-8 — the rule store is
    /// text this agent rendered, so anything else is a file it must not act
    /// on.
    fn read_file(&self, path: &Path) -> Result<Option<String>, FirewallError>;

    /// Writes `contents` to a NEW temporary file in `target`'s own directory
    /// and answers where it put it.
    ///
    /// The temporary file goes in the target's directory rather than in a
    /// scratch directory elsewhere, and that is a requirement rather than a
    /// preference: the apply finishes with a rename onto `target`, a rename
    /// is atomic only within one filesystem, and a rename across two
    /// filesystems fails outright (rules/rust.md "Config writes"). The
    /// directory is the agent's own and is not writable by any account, so it
    /// is as safe a place for a root-written temporary file as a scratch
    /// directory would be.
    ///
    /// The file is NOT flushed here — see [`FirewallHost::sync_file`] for why
    /// that is a step of its own.
    ///
    /// # Errors
    ///
    /// Returns [`FirewallError::StagingFailed`] when the directory cannot be
    /// determined or written to.
    fn stage_file(&self, target: &Path, contents: &str) -> Result<PathBuf, FirewallError>;

    /// Flushes the staged FILE to stable storage. Not its directory — see
    /// [`FirewallHost::sync_directory`], which is a separate step because it
    /// belongs on the other side of the rename.
    ///
    /// Its own step because the apply's order is stage, CHECK, flush, rename:
    /// there is no point paying for an `fsync` on a ruleset `nft` is about to
    /// refuse, and a test that pins the order has to be able to see where the
    /// flush happened. Doing it before the rename is what stops a crash from
    /// leaving a directory entry pointing at data that never reached the disk.
    ///
    /// # Errors
    ///
    /// Returns [`FirewallError::StagingFailed`] when the flush fails.
    fn sync_file(&self, staged: &Path) -> Result<(), FirewallError>;

    /// Atomically renames `staged` over `target`.
    ///
    /// # Errors
    ///
    /// Returns [`FirewallError::StagingFailed`] when the rename fails. The
    /// target is untouched in that case: a rename moves the whole directory
    /// entry or none of it.
    fn commit_file(&self, staged: &Path, target: &Path) -> Result<(), FirewallError>;

    /// Flushes the DIRECTORY holding `target`, so the rename that just
    /// happened survives a crash.
    ///
    /// **It is a separate method from [`FirewallHost::sync_file`] because it
    /// belongs on the other side of the rename, and the two flushes do
    /// different jobs.** `sync_file` makes the new file's CONTENTS durable and
    /// must run before the rename; this makes the DIRECTORY ENTRY that
    /// publishes those contents durable and is worthless before it — flushing
    /// a directory that does not yet hold the new entry writes out a state
    /// that predates it.
    ///
    /// Skipping it does not corrupt anything, which is what makes it easy to
    /// leave out: the failure is invisible until an unclean shutdown lands in
    /// the window, and then the path resolves to the OLD inode, so the
    /// packaged nftables service re-reads the previous ruleset at boot. A
    /// `deny_port` that reported success has silently re-opened its port.
    ///
    /// # Errors
    ///
    /// Returns [`FirewallError::StagingFailed`] when the directory cannot be
    /// determined, opened or flushed. The file at `target` is the new one by
    /// then; the same operation retried converges.
    fn sync_directory(&self, target: &Path) -> Result<(), FirewallError>;

    /// Removes a staged file that will not be committed.
    ///
    /// Infallible on purpose. It is called on the paths where the apply has
    /// already failed and is about to report why; a second failure here would
    /// replace a precise answer ("nft refused the ruleset, and here is what it
    /// said") with a vague one about a temporary file, and the caller can do
    /// nothing about either. What is left behind is one unreferenced file in a
    /// root-owned directory.
    fn discard_file(&self, staged: &Path);
}
