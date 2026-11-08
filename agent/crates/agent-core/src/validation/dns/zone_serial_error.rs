//! Why a candidate zone serial was refused.

/// Reasons [`super::zone_serial::ZoneSerial::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum ZoneSerialError {
    /// The candidate was zero, which is what an absent field decodes to.
    #[error("a zone serial cannot be 0")]
    Zero,
}
