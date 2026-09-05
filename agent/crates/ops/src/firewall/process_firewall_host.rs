//! The [`FirewallHost`] that actually touches this machine.

use std::fs;
use std::fs::File;
use std::io;
use std::io::Write as _;
use std::path::{Path, PathBuf};

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::spawn_argv::spawn_argv;

use crate::firewall::firewall_error::FirewallError;
use crate::firewall::firewall_host::FirewallHost;

/// The prefix every staged firewall file is created with.
///
/// A leading dot so the file is inconspicuous in a directory listing, and the
/// agent's own name so an operator who finds one after a crash knows what
/// wrote it and can delete it. `tempfile` appends random characters, so two
/// applies racing each other could not collide even without the lock every
/// mutation holds.
const STAGED_PREFIX: &str = ".maran-firewall-";

/// Runs the real `nft`, and replaces the real ruleset files.
///
/// The only implementation that touches the machine, and deliberately the
/// smallest piece of the area: every decision worth reviewing — which
/// subcommand, which order, what each exit status means — lives in the
/// operations, where it is tested against a fake. What is left here is
/// spawning a process, writing a file, flushing it and renaming it.
pub struct ProcessFirewallHost;

impl ProcessFirewallHost {
    /// Creates the host.
    #[must_use]
    pub fn new() -> Self {
        Self
    }
}

impl Default for ProcessFirewallHost {
    /// The host has no state, so the default is the only value there is.
    fn default() -> Self {
        Self::new()
    }
}

impl FirewallHost for ProcessFirewallHost {
    /// Spawns `program` with `arguments` as an argv array and captures both
    /// output streams.
    ///
    /// No shell is involved, at any point (rules/security.md item 3): the
    /// arguments reach `execve` one by one, so there is no command line for
    /// anything to re-parse — which matters more here than almost anywhere
    /// else, because `nft` has a grammar of its own and the braces around a
    /// set element are tokens in it. `program` comes from the
    /// `DistroAdapter`'s allow-list and never from a request.
    ///
    /// The spawn itself is [`spawn_argv`], shared with every other host that
    /// runs an argv array: it gives the child a closed standard input, so an
    /// `nft` that decides to read from it fails instead of hanging a root
    /// daemon, and it pins `LC_ALL=C`, which this file did not do before — a
    /// gain, since this area reads `nft`'s own diagnostics back.
    ///
    /// # Errors
    ///
    /// Returns [`FirewallError::NftFailed`] when the program cannot be
    /// started or waited for, carrying the operating system's reason. A
    /// non-zero exit is not an error here — it is returned in the outcome for
    /// the caller to read.
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, FirewallError> {
        spawn_argv(program, arguments).map_err(|error| FirewallError::NftFailed {
            stderr: format!("could not run {program}: {error}"),
        })
    }

    /// Reads the file, treating "it is not there" as an answer.
    ///
    /// # Errors
    ///
    /// Returns [`FirewallError::RulesetUnreadable`] when the file exists and
    /// cannot be read, or does not hold UTF-8 — the rule store is text this
    /// agent rendered, so bytes that are not text are not a store it may act
    /// on.
    fn read_file(&self, path: &Path) -> Result<Option<String>, FirewallError> {
        match fs::read_to_string(path) {
            Ok(text) => Ok(Some(text)),
            Err(error) if error.kind() == io::ErrorKind::NotFound => Ok(None),
            Err(_) => Err(FirewallError::RulesetUnreadable),
        }
    }

    /// Writes `contents` to a fresh temporary file in `target`'s directory.
    ///
    /// The directory is the target's own, because the apply ends in a rename
    /// onto `target` and a rename is atomic only within one filesystem —
    /// staging under `/run` and renaming into `/etc` would fail outright on
    /// every host where those are different filesystems, which is every host
    /// this panel supports. The directory belongs to the agent and no account
    /// can write it, so a root-written temporary file there is as safe as one
    /// in a dedicated scratch directory.
    ///
    /// The file keeps `tempfile`'s `0600`, and so does the ruleset once the
    /// rename lands. Only root reads it: `nft` runs as root, and so does the
    /// packaged nftables service that re-reads it at boot.
    ///
    /// Nothing is flushed here — see [`FirewallHost::sync_file`] for the
    /// file's flush and [`FirewallHost::sync_directory`] for the directory's,
    /// which sit on opposite sides of the rename.
    ///
    /// # Errors
    ///
    /// Returns [`FirewallError::StagingFailed`] when `target` has no parent
    /// directory, or the file cannot be created or written.
    fn stage_file(&self, target: &Path, contents: &str) -> Result<PathBuf, FirewallError> {
        let directory = target.parent().ok_or(FirewallError::StagingFailed)?;

        let mut staged = tempfile::Builder::new()
            .prefix(STAGED_PREFIX)
            .tempfile_in(directory)
            .map_err(|_| FirewallError::StagingFailed)?;
        staged
            .write_all(contents.as_bytes())
            .map_err(|_| FirewallError::StagingFailed)?;

        // `keep` gives up the RAII cleanup, which is the point: the file has
        // to outlive this call so that `nft --check` can read it by path. The
        // apply removes it on every path that does not commit it.
        let (_, path) = staged.keep().map_err(|_| FirewallError::StagingFailed)?;

        Ok(path)
    }

    /// Flushes the staged file, and nothing else.
    ///
    /// The directory it sits in is flushed by
    /// [`FirewallHost::sync_directory`] AFTER the rename, because that is the
    /// only side of the rename on which a directory flush does anything.
    ///
    /// # Errors
    ///
    /// Returns [`FirewallError::StagingFailed`] when the flush fails.
    fn sync_file(&self, staged: &Path) -> Result<(), FirewallError> {
        flush(staged)
    }

    /// Renames the staged file over the target.
    ///
    /// # Errors
    ///
    /// Returns [`FirewallError::StagingFailed`] when the rename fails. The
    /// target is untouched in that case: `rename` moves the whole directory
    /// entry or none of it.
    fn commit_file(&self, staged: &Path, target: &Path) -> Result<(), FirewallError> {
        fs::rename(staged, target).map_err(|_| FirewallError::StagingFailed)
    }

    /// Opens the directory holding `target` and flushes it, so the rename
    /// that has just happened is on the disk and not only in the page cache.
    ///
    /// A directory is flushed by opening it and `fsync`ing the descriptor like
    /// any other; a read-only descriptor is enough, which is why it is merely
    /// opened.
    ///
    /// # Errors
    ///
    /// Returns [`FirewallError::StagingFailed`] when `target` has no parent
    /// directory, or it cannot be opened or flushed.
    fn sync_directory(&self, target: &Path) -> Result<(), FirewallError> {
        let directory = target.parent().ok_or(FirewallError::StagingFailed)?;

        flush(directory)
    }

    /// Removes a staged file, ignoring every reason it might not come away.
    ///
    /// Deliberately infallible — see the trait's own note. The caller is on a
    /// path that has already failed and is about to report a precise reason;
    /// replacing that reason with one about a temporary file would help
    /// nobody.
    fn discard_file(&self, staged: &Path) {
        let _ = fs::remove_file(staged);
    }
}

/// Opens `path` and flushes it to stable storage.
///
/// Works for a directory as well as a file: `fsync` takes any descriptor, and
/// a read-only one is enough, which is why the file is merely opened rather
/// than re-opened for writing.
///
/// # Errors
///
/// Returns [`FirewallError::StagingFailed`] when the path cannot be opened or
/// flushed.
fn flush(path: &Path) -> Result<(), FirewallError> {
    File::open(path)
        .and_then(|handle| handle.sync_all())
        .map_err(|_| FirewallError::StagingFailed)
}
