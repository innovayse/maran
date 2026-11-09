//! Why a candidate CAA tag was refused.

/// Reasons [`super::caa_tag::CaaTag::parse`] refuses a candidate. The refused
/// text is not carried: it is caller text.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum CaaTagError {
    /// Not `issue`, `issuewild` or `iodef`.
    #[error("a CAA tag must be issue, issuewild or iodef")]
    Unknown,
}
