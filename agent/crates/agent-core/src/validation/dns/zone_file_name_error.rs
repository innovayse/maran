//! Why a directory entry is not a zone file.

/// Reasons [`super::zone_file_name::ZoneFileName::parse`] refuses a name.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum ZoneFileNameError {
    /// No `.db` suffix, or a stem that is not a zone name.
    #[error("not a zone file name")]
    NotAZoneFile,

    /// A zone name, but not spelled the way the agent writes it.
    #[error("a zone file name must be the lowercase zone name and .db")]
    NotCanonical,
}
