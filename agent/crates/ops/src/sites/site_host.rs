//! The seam between the site operations and the machine they run on.

use std::path::{Path, PathBuf};

use maran_agent_core::validation::system::name::AccountName;

use crate::safe_write::model::{Reload, Validator};
use crate::sites::SitesOpError;

/// The operating-system operations the site module needs on top of the
/// config-write protocol's own.
///
/// One seam covers everything an operation does to the machine — reading a
/// vhost, writing one through the config-write protocol, removing one, and
/// creating directories as the customer — so an operation cannot reach the
/// filesystem or a process by taking a different route, and one fake covers
/// all of it in a test.
///
/// The write and remove methods are on the seam rather than called directly
/// because the vhost directory is `/etc/maran/nginx/sites`: a test that
/// exercised the real protocol would have to be root and would reload a live
/// web server. The protocol itself is tested in `safe_write`; what is tested
/// here is which content each operation decides to write, and when it decides
/// to write nothing at all.
///
/// A trait and not direct `std::fs`/`Command` calls for the same reason
/// `accounts::SystemHost` is one: reloading a live web server and creating a
/// directory inside a real customer's home are exactly the things a unit test
/// must never actually do. The one implementation that touches the machine is
/// [`super::ProcessSiteHost`].
pub trait SiteHost: Send + Sync {
    /// Reads the vhost at `path`, or reports that there is none.
    ///
    /// The content IS the state this area keeps: a site is enabled or
    /// suspended according to what its vhost says, not according to a marker
    /// file that can survive the config it describes. That is what lets
    /// `enable_site` and `disable_site` converge instead of toggling.
    ///
    /// # Errors
    ///
    /// Returns [`SitesOpError::ConfigUnreadable`] when the file exists but
    /// cannot be read — which must not be mistaken for "no site here", since
    /// that reading would have `create_site` overwrite a live vhost.
    fn read_config(&self, path: &Path) -> Result<Option<String>, SitesOpError>;

    /// Lists every vhost file the web server is served from, in the agent's
    /// own include directory.
    ///
    /// The enumeration exists so that "which sites does this account have?"
    /// can be answered by the MACHINE rather than by a list the caller passes
    /// in. A list can only describe what the panel remembers creating, and a
    /// vhost it has forgotten is precisely the one still serving a suspended
    /// customer's site.
    ///
    /// Order is not part of the contract; a caller that needs one sorts.
    ///
    /// # Errors
    ///
    /// Returns [`SitesOpError::ConfigUnreadable`] naming the directory when it
    /// cannot be listed. Distinguishing that from an empty directory is the
    /// caller's responsibility and matters: both would otherwise read as "this
    /// account serves nothing".
    fn list_config_paths(&self) -> Result<Vec<PathBuf>, SitesOpError>;

    /// Creates the root-owned directory `account`'s site logs live in,
    /// `AgentPaths::account_site_log_dir`, `root:root 0750`.
    ///
    /// **As root, and NOT through [`Self::create_directories_as_account`].**
    /// That reverses this area's usual rule — a customer path is touched under
    /// the account's uid or not at all — and the reversal is the whole fix this
    /// method exists for. The rule is there so root never follows a symlink a
    /// customer planted; this directory is outside every home, under an
    /// ancestor chain (`/var/log/maran`, `root:maran 0750`) no unprivileged uid
    /// can write to or even traverse, so there is nothing for a customer to
    /// plant. While these logs lived at `/home/<account>/logs` the customer
    /// owned the directory and the ROOT nginx master opened files in it without
    /// `O_NOFOLLOW`, which was a proved root compromise: see
    /// `docs/superpowers/notes/2026-09-09-site-logs-threat-note.md`.
    ///
    /// Idempotent: an existing directory is success, which is what lets every
    /// vhost-rendering operation call it unconditionally.
    ///
    /// Implementations MUST set the mode explicitly rather than inherit a
    /// umask, and MUST create it as root.
    ///
    /// # Errors
    ///
    /// Returns [`SitesOpError::LogDirectory`] when the directory cannot be
    /// created or its mode cannot be set. This is fatal to the calling
    /// operation and not a warning: `nginx -t` opens the error-log target, so a
    /// vhost naming a missing directory is refused by the validator.
    fn create_site_log_directory(&self, account: &AccountName) -> Result<(), SitesOpError>;

    /// Creates `directories`, and every missing parent, running as `account`.
    ///
    /// The document root is inside a customer's home, so it is created by a
    /// process that has dropped to the account's uid and gid — never by the
    /// root daemon (rules/security.md: *direct `std::fs` on customer paths as
    /// root is forbidden*). A symlink already planted in the home therefore
    /// reaches a process that cannot follow it anywhere interesting.
    ///
    /// The site's LOG directory is deliberately not among these any more; it is
    /// [`Self::create_site_log_directory`]'s, and the reason is that method's
    /// doc comment.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`: the
    /// underlying `fork_as_account` forks and blocks in `waitpid`, which on a
    /// runtime worker stalls every other in-flight command.
    ///
    /// # Errors
    ///
    /// Returns [`SitesOpError::DocumentRoot`] when the account cannot be
    /// resolved, the privilege drop fails or does not fully apply, or the
    /// child cannot create a directory.
    fn create_directories_as_account(
        &self,
        account: &AccountName,
        directories: &[&Path],
    ) -> Result<(), SitesOpError>;

    /// Writes `contents` to `target` through the config-write protocol:
    /// temporary file beside the target, `fsync`, atomic rename, `validator`,
    /// `reload`, and a restoration of the previous content if either refuses
    /// (rules/rust.md "Config writes"). The one implementation delegates to
    /// `crate::safe_write::write_config` and adds nothing of its own.
    ///
    /// # Errors
    ///
    /// Returns [`SitesOpError::NginxValidation`] or
    /// [`SitesOpError::ReloadFailed`] with the previous vhost restored, and
    /// [`SitesOpError::ConfigWrite`] for every other failure of the protocol.
    fn write_config(
        &self,
        target: &Path,
        contents: &str,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), SitesOpError>;

    /// Removes `target` through the same protocol, validating and reloading
    /// after the unlink and putting the file back if either refuses.
    ///
    /// # Errors
    ///
    /// As [`Self::write_config`].
    fn remove_config(
        &self,
        target: &Path,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), SitesOpError>;

    /// Resolves `relative` inside `account`'s home and proves it is contained
    /// there, returning the canonical path to use from then on.
    ///
    /// Containment is decided by the filesystem, after the directory exists,
    /// not by inspecting the path text: a `sites/` directory replaced by a
    /// symlink to `/etc` looks contained in every string comparison and is
    /// not. The canonical answer is what the vhost is rendered with, so the
    /// checked path and the used path are the same path — resolving and then
    /// reopening by the original name would reintroduce the race the check
    /// exists to close.
    ///
    /// # Errors
    ///
    /// Returns [`SitesOpError::UnsafeDocumentRoot`] when the path does not
    /// exist or resolves outside the account's home.
    fn resolve_in_account_home(
        &self,
        account: &AccountName,
        relative: &Path,
    ) -> Result<PathBuf, SitesOpError>;
}
