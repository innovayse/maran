//! The seam between the app operations and the machine they run on.

use std::path::Path;

use crate::apps::AppsOpError;
use crate::php::PhpHost;

/// The operating-system operations the app store needs on top of the PHP area's.
///
/// A supertrait of [`PhpHost`] because installing a PHP version IS an app install and reuses the
/// PHP area's operation whole, and because a config write, a directory check and a program run
/// are the same acts there: one implementation (`ProcessPhpHost`) and one fake cover both.
pub trait AppsHost: PhpHost {
    /// Whether `path` exists (a file or a directory). How "is nginx installed?" is answered: the
    /// package's binary is there or it is not, asked of the filesystem and not of the package
    /// manager, because the listing is called on every page load and a package database query
    /// per app per load is a lock contended with every real installation.
    fn path_exists(&self, path: &Path) -> bool;

    /// The names of the entries of the directory `path`: an empty list when it is absent (a
    /// directory that was never made holds nothing). Used to learn whether the agent's vhost
    /// directory of a web server, a pool directory or the unit directory holds anything that
    /// uses an app.
    ///
    /// # Errors
    ///
    /// Returns [`AppsOpError::HostUnreadable`] when the directory exists and cannot be read:
    /// every caller decides a destructive step on the answer, so "cannot look" must not read as
    /// "nothing there".
    fn directory_entries(&self, path: &Path) -> Result<Vec<String>, AppsOpError>;

    /// Writes the agent's own record `contents` at `path`, atomically (a temporary file beside
    /// it, flushed, renamed) and with no validator or reload: nothing checks the file but the
    /// agent's own reader. Used for the agent's own records: the site web server marker, and the panel's port-80 block while it is parked.
    ///
    /// # Errors
    ///
    /// Returns [`AppsOpError::Php`] with the write protocol's refusal; the previous content is
    /// then still in place.
    fn write_record(&self, path: &Path, contents: &str) -> Result<(), AppsOpError>;

    /// Removes the agent's own record at `path`. Absent is a success: clearing a marker that was
    /// never written is the state the caller wants.
    ///
    /// # Errors
    ///
    /// Returns [`AppsOpError::Php`] when the file exists and could not be removed.
    fn remove_record(&self, path: &Path) -> Result<(), AppsOpError>;
}
