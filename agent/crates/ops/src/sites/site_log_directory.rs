//! A site-log directory held open, as the traffic summary and prune use it.

use crate::sites::SitesOpError;
use crate::sites::site_log_file::SiteLogFile;

/// A directory of site logs, opened once and proven root's.
///
/// Every name an implementation is handed is reached through the one held
/// descriptor, never through a path, so a rename of an intermediate component
/// cannot redirect a running summary or prune.
pub trait SiteLogDirectory {
    /// Opens the entry `name` as a log file; `None` when there is no such entry.
    ///
    /// # Errors
    ///
    /// Returns [`SitesOpError::LogUnreadable`] when the entry exists but is not
    /// a regular file root owns with a single link.
    fn open_file(&self, name: &str) -> Result<Option<Box<dyn SiteLogFile>>, SitesOpError>;

    /// The names of the entries in the directory, in no particular order.
    ///
    /// # Errors
    ///
    /// Returns [`SitesOpError::LogUnreadable`] when the directory cannot be read.
    fn list_names(&self) -> Result<Vec<String>, SitesOpError>;

    /// Removes the regular file `name`. Returns `true` when a file was
    /// removed and `false` when there was nothing to remove: an entry that is
    /// gone, or one that is not a regular file (a directory, a symlink), which
    /// is left exactly as it is.
    ///
    /// # Errors
    ///
    /// Returns [`SitesOpError::LogUnreadable`] when the entry exists and is a
    /// regular file but cannot be removed.
    fn remove_file(&self, name: &str) -> Result<bool, SitesOpError>;
}
