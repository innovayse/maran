//! The seam between the PHP operations and the machine they run on.

use std::path::Path;

use maran_agent_core::validation::name::AccountName;

use crate::php::PhpOpError;
use crate::safe_write::ConfigHost;
use crate::safe_write::model::{Reload, Validator};

/// The operating-system operations the PHP module needs on top of the
/// config-write protocol's own.
///
/// A supertrait of [`ConfigHost`] rather than a separate seam: installing a
/// package and reloading php-fpm are the same act — spawning an allow-listed
/// absolute path with an argv array — so they share one `run`, and one fake
/// covers the whole area.
///
/// The write method is on the seam rather than calling
/// `crate::safe_write::write_config` directly for the reason the site area
/// gives: the pool directory is a real root-owned path and the reload restarts
/// a live php-fpm, so a test that exercised the real protocol would have to be
/// root. The protocol itself is tested in `safe_write`; what is tested here is
/// which content each operation decides to write.
///
/// The one implementation that touches the machine is
/// [`super::ProcessPhpHost`].
pub trait PhpHost: ConfigHost {
    /// Whether `path` exists and is a directory.
    ///
    /// This is how "is PHP 8.3 installed?" is answered: the version's pool
    /// directory is created by its `-fpm` package and by nothing else, so its
    /// presence is the package's presence. Asked of the filesystem rather than
    /// of the package manager deliberately — `list_php_versions` is called on
    /// every panel page load, and `dpkg-query`/`rpm -q` per version per load
    /// is a package database lock contended with every real installation.
    fn directory_exists(&self, path: &Path) -> bool;

    /// Creates `path` and every missing parent, owned by root, with `mode`.
    ///
    /// Used only for the agent's own socket directory, which is outside every
    /// account's home — a customer path is never created from here, only by
    /// [`Self::create_directories_as_account`] after a privilege drop
    /// (rules/security.md).
    ///
    /// `mode` is explicit rather than inherited from the process umask.
    /// `create_dir_all` alone yields `0o777 & !umask`, which is fine at the
    /// usual `022` and world-writable under a umask of zero — and a
    /// world-writable, non-sticky socket directory lets one account unlink a
    /// neighbour's socket and bind its own in its place, which is request
    /// interception, not merely a nuisance.
    ///
    /// # Errors
    ///
    /// Returns [`PhpOpError::ConfigWrite`] when the directory cannot be
    /// created or its mode cannot be set.
    fn create_directory(&self, path: &Path, mode: u32) -> Result<(), PhpOpError>;

    /// Creates `directories`, and every missing parent, running as `account`,
    /// with `mode` on each.
    ///
    /// The session and upload directories are inside a customer's home, so
    /// they are created by a process that has dropped to the account's uid and
    /// gid — never by the root daemon (rules/security.md: *direct `std::fs` on
    /// customer paths as root is forbidden*). Creating them as root would also
    /// defeat the point: PHP running as the account has to be able to write
    /// them, and a root-owned session directory is exactly the condition that
    /// makes PHP fall back to the shared `/tmp` this pool refuses to grant.
    ///
    /// `mode` is explicit here for a sharper reason than it is on
    /// [`Self::create_directory`]. **A PHP session filename IS the session
    /// ID.** A world-listable session directory therefore hands a session over
    /// to anyone who can run `ls` in it — no file content need be read at all.
    /// Left to the forked child's inherited umask this would typically be
    /// `0755`, which is world-traversable and world-listable, and the
    /// cross-tenant hole closed by moving sessions out of `/tmp` would reopen
    /// through the directory listing instead.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`: the
    /// underlying `fork_as_account` forks and blocks in `waitpid`, which on a
    /// runtime worker stalls every other in-flight command.
    ///
    /// # Errors
    ///
    /// Returns [`PhpOpError::ConfigWrite`] when the account cannot be
    /// resolved, the privilege drop fails or does not fully apply, or the
    /// child cannot create a directory or set its mode.
    fn create_directories_as_account(
        &self,
        account: &AccountName,
        directories: &[&Path],
        mode: u32,
    ) -> Result<(), PhpOpError>;

    /// Writes `contents` to `target` through the config-write protocol:
    /// temporary file beside the target, `fsync`, atomic rename, `validator`,
    /// `reload`, and a restoration of the previous content if either refuses
    /// (rules/rust.md "Config writes"). The one implementation delegates to
    /// `crate::safe_write::write_config` and adds nothing of its own.
    ///
    /// # Errors
    ///
    /// Returns [`PhpOpError::PoolValidation`] or [`PhpOpError::ReloadFailed`]
    /// with the previous pool restored, and [`PhpOpError::ConfigWrite`] for
    /// every other failure of the protocol.
    fn write_config(
        &self,
        target: &Path,
        contents: &str,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), PhpOpError>;

    /// Removes `target` through the same protocol, in reverse: capture the
    /// current content, unlink, `validator`, `reload`, and put the bytes back
    /// if either refuses. The one implementation delegates to
    /// `crate::safe_write::remove_config` and adds nothing of its own.
    ///
    /// On the seam beside [`Self::write_config`] and not called directly for
    /// the same reason: the pool directory is a real root-owned path and the
    /// reload restarts a live php-fpm.
    ///
    /// A target that is already absent is a success with NOTHING run — no
    /// validator, no reload. That is what makes removing a pool idempotent for
    /// a caller that does not know whether one was ever written, which is every
    /// caller: a pool exists only if the account ever used that version.
    ///
    /// # Errors
    ///
    /// Returns [`PhpOpError::PoolValidation`] or [`PhpOpError::ReloadFailed`]
    /// with the pool restored, and [`PhpOpError::ConfigWrite`] for every other
    /// failure of the protocol.
    fn remove_config(
        &self,
        target: &Path,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), PhpOpError>;
}
