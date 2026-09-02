//! The seam between the SFTP operations and the machine they run on.

use std::path::Path;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;

use crate::safe_write::model::{Reload, Validator};
use crate::sftp::model::account_ownership::AccountOwnership;
use crate::sftp::sftp_error::SftpError;

/// The operating-system operations the SFTP area needs.
///
/// A trait rather than direct calls to `std::process::Command` and `std::fs`,
/// and not for abstraction's sake: creating a system login, setting its
/// password and installing a mount unit are exactly the operations a unit test
/// must never really perform. Behind this seam every decision — which tool,
/// which arguments, what goes to standard input rather than to the argument
/// vector, what each exit status means — is testable, and the one
/// implementation that really touches the machine stays small enough to read in
/// full.
///
/// Implementations MUST spawn with an argv array against an absolute path taken
/// from the `DistroAdapter`, never through a shell and never through a program
/// name resolved by `PATH` (rules/security.md item 3).
pub trait SftpHost: Send + Sync {
    /// Runs `program` with `arguments`, optionally writing `stdin` to its
    /// standard input, and waits for it.
    ///
    /// `stdin` exists for exactly one caller — `chpasswd`, which takes its
    /// `user:password` line there. A command line is world-readable through
    /// `/proc` on every host this panel supports, so a password passed as an
    /// argument is a password every local user can read; passing it through a
    /// pipe is what makes that impossible rather than merely unlikely.
    ///
    /// Implementations must close the pipe after writing, or a tool that reads
    /// to end of input never returns and takes a root daemon's task with it.
    ///
    /// # Errors
    ///
    /// Returns [`SftpError::SpawnFailed`] with a `code` of `-1` when the
    /// program cannot be started at all, or when its standard input cannot be
    /// written. A non-zero exit is NOT an error here — it is returned in the
    /// outcome, because each caller reads a status differently: `useradd`'s 9
    /// means "already there" while `userdel`'s 9 means nothing of the sort.
    fn run(
        &self,
        program: &str,
        arguments: &[&str],
        stdin: Option<&str>,
    ) -> Result<CommandOutcome, SftpError>;

    /// Looks up the numeric identity of the hosting account `account`.
    ///
    /// Behind the seam rather than a direct `getpwnam` in the operation, for
    /// the reason every other method here is: a unit test cannot put an account
    /// in the host's password database, and one that resolved a real name would
    /// pass or fail on whichever accounts the machine running it happens to
    /// have.
    ///
    /// The answer is what the SFTP login is created with — see
    /// [`AccountOwnership`] for why a login shares its account's ids instead of
    /// having its own.
    ///
    /// # Errors
    ///
    /// Returns [`SftpError::AccountMissing`] when the password database holds
    /// no such account, or cannot be read.
    fn account_ownership(&self, account: &AccountName) -> Result<AccountOwnership, SftpError>;

    /// Creates `path` and every missing parent, at `mode`, owned by root.
    ///
    /// Idempotent: a directory that is already there is success, because the
    /// jail is ensured on every creation and the second SFTP user of an account
    /// must not fail on the first one's work.
    ///
    /// `mode` is applied explicitly rather than left to the process umask.
    /// OpenSSH refuses to chroot into a directory that is group- or
    /// world-writable, so the mode is not decoration here — it is the difference
    /// between a working login and a daemon that disconnects every SFTP session
    /// with a message the customer cannot act on.
    ///
    /// # Errors
    ///
    /// Returns [`SftpError::JailFailed`] when the directory cannot be created or
    /// its mode cannot be set.
    fn create_directory(&self, path: &Path, mode: u32) -> Result<(), SftpError>;

    /// Writes `contents` to `target` through the config-write protocol:
    /// temporary file beside the target, `fsync`, atomic rename, `validator`,
    /// `reload`, and a restoration of the previous content if either refuses
    /// (rules/rust.md "Config writes"). The one implementation delegates to
    /// `crate::safe_write::write_config` and adds nothing of its own.
    ///
    /// # Errors
    ///
    /// Returns [`SftpError::JailFailed`] for every failure of the protocol —
    /// the unit is the jail, and a jail that did not take effect is the same
    /// condition however the protocol arrived at it.
    fn write_config(
        &self,
        target: &Path,
        contents: &str,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), SftpError>;

    /// Lists every SFTP login on this host whose name belongs to `account`.
    ///
    /// `passwd_database` is the path the `DistroAdapter` gives for the host's
    /// local password database, passed in rather than known here for the same
    /// reason `run` is given an absolute program path: `ops` names no platform
    /// location of its own.
    ///
    /// Behind the seam for the reason `account_ownership` is: a unit test
    /// cannot put logins in the host's password database, and one that read the
    /// real database would pass or fail on whichever accounts the machine
    /// running it happens to have.
    ///
    /// An implementation decodes each name the way
    /// [`SftpUserName::for_account`] built it — at the LAST separator, matching
    /// the WHOLE account — and rebuilds it through that constructor, so a name
    /// this method reports is a name this agent could itself have created. A
    /// prefix scan would be the wrong predicate: `alice_` is a prefix of
    /// `alice_bob_deploy`, which is account `alice_bob`'s login.
    ///
    /// **The name alone is not enough, and this is the trap.** Account names may
    /// contain the separator, so the ACCOUNT `alice_bob` and the login `bob` of
    /// account `alice` are the same eleven characters: no decode of the name can
    /// tell them apart, and one that tried would delete a neighbouring account's
    /// system user as a side effect of removing this account's logins. It has
    /// happened — the polygon caught it.
    ///
    /// `jail_directory` is what settles it. Every SFTP login this agent creates
    /// has its passwd home set to its account's jail, while a hosting account's
    /// home is under `/home`; so a candidate is a login of this account only
    /// when its home is exactly this account's jail. The discriminator is a
    /// value the agent itself wrote, not a guess about a name.
    ///
    /// The account's OWN system user is therefore never among the answers, on
    /// two independent counts: it carries no separator after the account name,
    /// and its home is not the jail.
    ///
    /// # Errors
    ///
    /// Returns [`SftpError::AccountMissing`] when the password database cannot
    /// be read at all. An account with no logins is an empty list and not an
    /// error.
    fn account_logins(
        &self,
        passwd_database: &str,
        account: &AccountName,
        jail_directory: &str,
    ) -> Result<Vec<SftpUserName>, SftpError>;

    /// Reports whether anything exists at `path`.
    ///
    /// Asked of exactly one thing: whether an account's mount unit was ever
    /// installed. `systemctl disable` refuses a unit it has no file for, so an
    /// account whose jail was never built would otherwise fail its own deletion
    /// over a unit that was correctly never written.
    ///
    /// It answers `false` for a path that cannot be inspected as well as for one
    /// that is not there, and that is deliberate: the only caller uses the
    /// answer to decide whether to ASK the service manager, and the service
    /// manager's own refusal is what would then be reported.
    fn path_exists(&self, path: &Path) -> bool;

    /// Removes the file at `path`.
    ///
    /// Idempotent: a file that is not there is success, because a deletion
    /// retried after a lost response must converge rather than fail on its own
    /// previous work.
    ///
    /// # Errors
    ///
    /// Returns [`SftpError::JailFailed`] when the file is there and cannot be
    /// removed.
    fn remove_file(&self, path: &Path) -> Result<(), SftpError>;

    /// Removes the directory at `path`, and only if it is empty.
    ///
    /// **Empty-only is the security property of this method, not a
    /// limitation.** The directories it is asked for are an account's jail and
    /// the mount point inside it, and that mount point holds the account's real
    /// home for as long as the bind mount is in place. A recursive removal here
    /// would walk into the mount and delete the customer's entire website; a
    /// removal that refuses a non-empty directory cannot, and its refusal is
    /// exactly the signal that the unmount did not happen.
    ///
    /// Idempotent: a directory that is not there is success, for the same reason
    /// [`SftpHost::remove_file`] is.
    ///
    /// # Errors
    ///
    /// Returns [`SftpError::JailFailed`] when the directory is there and cannot
    /// be removed — including when it is not empty, which on the mount point
    /// means the mount is still in place.
    fn remove_directory(&self, path: &Path) -> Result<(), SftpError>;
}
