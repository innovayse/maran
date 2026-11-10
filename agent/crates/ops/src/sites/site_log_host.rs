//! The seam to the machine for the traffic summary and the log prune.

use maran_agent_core::validation::system::name::AccountName;

use crate::sites::SitesOpError;
use crate::sites::site_log_directory::SiteLogDirectory;

/// Opens an account's site-log directory.
///
/// A seam of its own beside [`super::SiteHost`] and
/// [`super::SiteMaintenanceHost`]: those are implemented by the SSL area's host
/// as well, and neither summarising nor pruning a log is something a
/// certificate installation does. The one implementation that touches the
/// machine is [`super::ProcessSiteLogHost`].
pub trait SiteLogHost: Send + Sync {
    /// Opens `/var/log/maran/sites/<account>` by path, once, and proves root
    /// owns it.
    ///
    /// # Errors
    ///
    /// Returns [`SitesOpError::LogUnreadable`] when the directory is missing,
    /// is a symlink, or is not a directory root owns.
    fn open_directory(
        &self,
        account: &AccountName,
    ) -> Result<Box<dyn SiteLogDirectory>, SitesOpError>;

    /// [`SiteLogHost::open_directory`], except that a directory that does not
    /// exist is `None` and not an error.
    ///
    /// For the removal of a deleted site's logs, where an account that never
    /// served a request has no directory and "nothing to remove" is the converged
    /// answer. Every other refusal (a symlink, a directory root does not own) is
    /// still an error: an absent directory is the only thing read as nothing.
    ///
    /// # Errors
    ///
    /// Returns [`SitesOpError::LogUnreadable`] as [`SiteLogHost::open_directory`]
    /// does, for anything but a missing directory.
    fn open_directory_if_present(
        &self,
        account: &AccountName,
    ) -> Result<Option<Box<dyn SiteLogDirectory>>, SitesOpError>;
}
