//! Why the byte cap of an upload was refused.

/// Rejection reasons for [`super::upload_limit::UploadLimit::parse`].
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum UploadLimitError {
    /// The cap is above the most the agent will ever accept for one upload.
    #[error("the upload cap is above the agent's ceiling")]
    AboveCeiling,
}
