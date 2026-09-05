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

    /// Creates `directories`, and every missing parent, running as `account`.
    ///
    /// The document root and the log directory are inside a customer's home,
    /// so they are created by a process that has dropped to the account's uid
    /// and gid — never by the root daemon (rules/security.md: *direct
    /// `std::fs` on customer paths as root is forbidden*). A symlink already
    /// planted in the home therefore reaches a process that cannot follow it
    /// anywhere interesting.
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
