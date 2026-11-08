//! Why the allowances of a copy were refused.

/// Rejection reasons for [`super::transfer_bounds::TransferBounds::parse`].
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum TransferBoundsError {
    /// A cap was zero: a copy always runs under a stated allowance.
    #[error("a transfer cap was not stated")]
    NotStated,
    /// A cap was above what the agent will ever allow.
    #[error("a transfer cap is above the agent's ceiling")]
    AboveCeiling,
}
