//! Why a DNSSEC key file name was refused.

/// Reasons [`super::dnssec_key_file_name::DnssecKeyFileName`] cannot be built.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum DnssecKeyFileNameError {
    /// The longest file of the key, `K<zone>.+013+<tag>.private`, would exceed
    /// the 255 bytes a file name may have.
    #[error("the zone name is too long to be the stem of a key file name")]
    TooLong,

    /// A directory entry that is not `K<zone>.+013+<tag>` followed by `.key`,
    /// `.private` or `.state`, spelled exactly as this crate writes it.
    #[error("not a DNSSEC key file name")]
    NotAKeyFile,
}
