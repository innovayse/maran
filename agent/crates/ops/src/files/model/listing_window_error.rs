//! Why a listing window was refused.

/// Rejection reasons for [`super::listing_window::ListingWindow::new`].
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum ListingWindowError {
    /// The page size was above the hard maximum.
    #[error("listing page size is above the maximum")]
    LimitTooLarge,
}
