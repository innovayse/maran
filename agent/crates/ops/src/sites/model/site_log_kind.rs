//! Which of a site's two logs is being read.

/// Which log stream a tail reads.
///
/// An enum and not a filename, because the filename is derived by
/// [`super::site_paths::SitePaths`] and must stay derived: a caller that could
/// name the file could name any file in the account's home, and the tail runs
/// in the root daemon.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SiteLogKind {
    /// The web server's access log for this site.
    Access,
    /// The web server's error log for this site.
    Error,
}
